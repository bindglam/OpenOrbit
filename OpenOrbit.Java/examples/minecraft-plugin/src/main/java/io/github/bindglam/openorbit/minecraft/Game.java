package io.github.bindglam.openorbit.minecraft;

import io.github.bindglam.openorbit.Chart;
import io.github.bindglam.openorbit.EngineEvent;
import io.github.bindglam.openorbit.GameEngine;
import io.github.bindglam.openorbit.HitMargin;
import io.github.bindglam.openorbit.HitResult;
import io.github.bindglam.openorbit.PlanetState;
import net.kyori.adventure.key.Key;
import net.kyori.adventure.sound.Sound;
import net.kyori.adventure.sound.SoundStop;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import net.kyori.adventure.text.format.TextColor;
import net.kyori.adventure.title.Title;
import org.bukkit.Input;
import org.bukkit.Location;
import org.bukkit.entity.Entity;
import org.bukkit.entity.Player;
import org.bukkit.plugin.Plugin;

import java.time.Duration;
import java.util.EnumMap;
import java.util.Map;
import java.util.concurrent.TimeUnit;
import java.util.logging.Level;

/**
 * 플레이어 한 명의 한 판. 차트를 넘겨받아 {@link #close()} 때 같이 닫는다.
 * <p>
 * 스레드:
 * <ul>
 *   <li>엔진 스레드(네이티브): 판정(미드스핀 자동 통과, 미스, 오토플레이)을 곡 시계에 맞춰 돌린다.
 *       서버 틱이나 TPS 와 상관없다. 곡 시계도 엔진 안의 단조 시계다.</li>
 *   <li>이벤트 스레드: 엔진 이벤트를 기다렸다가 사운드/액션바/타이틀을 보내고, 트레일 파티클을 뿌린다.
 *       모두 패킷만 보내므로 메인 스레드를 거치지 않는다.</li>
 *   <li>메인 스레드: 입력({@link #press}, {@link #onInput}), 화면({@link #render}), 시작/정리.
 *       엔티티는 메인 스레드에서만 움직일 수 있다.</li>
 * </ul>
 * 판정 카운트와 종료 시각은 이벤트 스레드만 건드린다.
 */
final class Game {
    enum State { LOADING, PLAYING, FINISHED }

    private static final int TRAIL_INTERVAL_MS = 15;
    /** 클리어/실패 후 결과를 보여 주다가 끝내기까지의 시간. */
    private static final long FINISH_NANOS = TimeUnit.SECONDS.toNanos(3);
    private static final Key HITSOUND = Key.key("minecraft", "block.note_block.hat");

    private final Plugin plugin;
    private final Player player;
    private final String chartId;
    private final Chart chart;
    private final GameEngine engine;
    private final StageRenderer stage;
    private final PluginSettings settings;
    private final boolean autoplay;
    /** 입력 오프셋. 엔진 보정값에 들어가므로 화면은 이만큼 되돌려서 그린다. */
    private final double inputOffsetMs;

    private final Location returnLocation;
    private final boolean wasInvulnerable;

    /** 이벤트 스레드 전용. */
    private final Map<HitMargin, Integer> counts = new EnumMap<>(HitMargin.class);
    private long finishNanos;

    private volatile State state = State.LOADING;
    private volatile Key song;

    /** 메인 스레드 전용. */
    private Input lastInput;
    private Thread loop;
    private volatile boolean closing;
    /** 게임 스레드가 끝까지 보여 줬으니 정리해 달라고 표시한다. */
    private volatile boolean done;

    Game(Plugin plugin, Player player, String chartId, Chart chart, PluginSettings settings, boolean autoplay, double inputOffsetMs) {
        this.plugin = plugin;
        this.player = player;
        this.chartId = chartId;
        this.chart = chart;
        this.settings = settings;
        this.autoplay = autoplay;
        // 오토플레이는 정확한 시각에 치므로 입력 오프셋이 필요 없다
        this.inputOffsetMs = autoplay ? 0 : inputOffsetMs;
        this.engine = new GameEngine(chart, settings.windows());
        engine.setAutoplay(autoplay ? GameEngine.Autoplay.PERFECT : GameEngine.Autoplay.NONE);
        engine.setNoFail(settings.noFail());
        // 오디오 지연은 판정과 화면 모두에, 입력 오프셋은 판정에만 적용한다 (화면은 render 에서 되돌린다)
        engine.setCalibrationMs(settings.audioLatencyMs() + this.inputOffsetMs);

        returnLocation = player.getLocation();
        wasInvulnerable = player.isInvulnerable();
        lastInput = player.getCurrentInput();

        var world = player.getWorld();
        var origin = returnLocation.toBlockLocation();
        origin.setY(Math.min(origin.getY() + settings.stageHeight(), world.getMaxHeight() - settings.cameraHeight() - 4));
        stage = new StageRenderer(plugin, player, chart, origin, settings);
        stage.mount();
        player.setInvulnerable(true);
        render();
    }

    Player player() {
        return player;
    }

    boolean isCamera(Entity entity) {
        return stage.isCamera(entity);
    }

    /** 결과까지 다 보여 줘서 정리해야 하면 true. */
    boolean isDone() {
        return done;
    }

    /** 곡을 틀고 엔진과 이벤트 스레드를 시작한다. song 이 null 이면 음악 없이 진행한다. 메인 스레드에서 부른다. */
    void begin(Key song) {
        if (state != State.LOADING || closing)
            return;

        this.song = song;
        if (song != null) {
            var pitch = (float) Math.clamp(chart.settings().pitch() / 100.0, 0.5, 2.0);
            player.playSound(Sound.sound(song, Sound.Source.MASTER, 1f, pitch), Sound.Emitter.self());
        }
        state = State.PLAYING;
        engine.start();
        player.sendActionBar(Component.text(chartId, NamedTextColor.GRAY));

        loop = Thread.ofPlatform()
                .name("OpenOrbit Game - " + player.getName())
                .daemon()
                .start(this::runLoop);
    }

    // ---------- 이벤트 스레드 ----------

    private void runLoop() {
        try {
            var nextTrail = 0L;
            while (!closing && !done) {
                // 이벤트가 오면 바로 깨고, 없으면 트레일 주기마다 깬다
                engine.awaitEvents(TRAIL_INTERVAL_MS, this::onEvent);

                var now = System.nanoTime();
                if (state == State.FINISHED) {
                    if (now - finishNanos >= FINISH_NANOS)
                        done = true;
                } else if (now >= nextTrail) {
                    nextTrail = now + TimeUnit.MILLISECONDS.toNanos(TRAIL_INTERVAL_MS);
                    stage.emitTrail(engine.planetState(displayTimeMs()));
                }
            }
        } catch (RuntimeException e) {
            plugin.getLogger().log(Level.SEVERE, "이벤트 스레드에서 오류가 나서 종료합니다", e);
            done = true;
        }
    }

    private void onEvent(EngineEvent event) {
        if (state != State.PLAYING)
            return;
        switch (event) {
            case EngineEvent.Hit hit -> judge(hit.result());
            case EngineEvent.Cleared cleared -> clear();
            case EngineEvent.Failed failed -> fail();
        }
    }

    // ---------- 메인 스레드 ----------

    /** 이동 키 입력. 새로 눌린 키마다 한 번씩 친다 (얼불춤처럼 아무 키나 입력으로 받는다). */
    void onInput(Input input) {
        var pressed = rising(lastInput.isForward(), input.isForward())
                + rising(lastInput.isBackward(), input.isBackward())
                + rising(lastInput.isLeft(), input.isLeft())
                + rising(lastInput.isRight(), input.isRight())
                + rising(lastInput.isJump(), input.isJump())
                + rising(lastInput.isSneak(), input.isSneak())
                + rising(lastInput.isSprint(), input.isSprint());
        lastInput = input;

        for (var i = 0; i < pressed; i++)
            press();
    }

    /** 지금 시각으로 한 번 친다. 판정 결과는 이벤트 스레드로 온다. */
    void press() {
        if (autoplay || state != State.PLAYING)
            return;
        engine.press();
    }

    /** 게임 스레드가 정한 상태를 엔티티에 반영한다. 매 서버 틱 메인 스레드에서 부른다. */
    void render() {
        var floor = engine.currentFloor();
        PlanetState planets = engine.planetState(displayTimeMs() + StageRenderer.INTERPOLATION_TICKS * 50.0);
        stage.render(floor, planets);
    }

    /** 이벤트 스레드와 엔진을 멈추고 엔티티를 지운 뒤 플레이어를 원래 자리로 돌려보낸다. */
    void close() {
        closing = true;
        var thread = loop;
        if (thread != null) {
            try {
                thread.join(1000);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
            }
        }

        stopSong();
        stage.close();
        engine.close();
        chart.close();
        player.setInvulnerable(wasInvulnerable);
        if (player.isOnline())
            player.teleport(returnLocation);
    }

    // ---------- 판정 (이벤트 스레드) ----------

    /** 화면에 쓰는 곡 시각(ms). 엔진 시각에서 입력 오프셋만 되돌려 실제로 소리가 들리는 시점에 맞춘다. */
    private double displayTimeMs() {
        return engine.currentTimeMs() + inputOffsetMs;
    }

    private void judge(HitResult result) {
        counts.merge(result.margin(), 1, Integer::sum);
        var margin = result.margin();

        // 실패는 뒤따라 오는 Failed 이벤트에서 처리한다
        if (margin != HitMargin.TOO_EARLY && margin != HitMargin.TOO_LATE && margin != HitMargin.AUTO)
            player.playSound(Sound.sound(HITSOUND, Sound.Source.MASTER, 0.7f, 1f), Sound.Emitter.self());
        if (margin != HitMargin.AUTO)
            player.sendActionBar(judgmentText(margin).append(progressText()));
    }

    private void clear() {
        finish();
        player.showTitle(Title.title(
                Component.text("클리어!", NamedTextColor.GOLD),
                summaryText(),
                Title.Times.times(Duration.ZERO, Duration.ofSeconds(3), Duration.ofMillis(500))));
    }

    private void fail() {
        finish();
        stopSong();
        player.showTitle(Title.title(
                Component.text("실패", NamedTextColor.RED),
                progressText().color(NamedTextColor.GRAY),
                Title.Times.times(Duration.ZERO, Duration.ofSeconds(2), Duration.ofMillis(500))));
    }

    private void finish() {
        finishNanos = System.nanoTime();
        state = State.FINISHED;
    }

    private void stopSong() {
        var current = song;
        song = null;
        if (current != null)
            player.stopSound(SoundStop.named(current));
    }

    // ---------- 텍스트 ----------

    private static int rising(boolean before, boolean after) {
        return !before && after ? 1 : 0;
    }

    private static Component judgmentText(HitMargin margin) {
        return switch (margin) {
            case PERFECT -> Component.text("Perfect!", TextColor.color(0x60FF4E));
            case EARLY_PERFECT -> Component.text("EPerfect!", TextColor.color(0xFCFF4E));
            case LATE_PERFECT -> Component.text("LPerfect!", TextColor.color(0xFCFF4E));
            case VERY_EARLY -> Component.text("Early!", TextColor.color(0xFF6F4E));
            case VERY_LATE -> Component.text("Late!", TextColor.color(0xFF6F4E));
            case TOO_EARLY -> Component.text("Too Early!", TextColor.color(0xFF0000));
            case TOO_LATE -> Component.text("Miss!", TextColor.color(0xFF0000));
            case AUTO -> Component.empty();
        };
    }

    private Component progressText() {
        var total = Math.max(1, chart.tiles().size() - 1);
        var percent = 100 * engine.currentFloor() / total;
        return Component.text("  " + percent + "%", NamedTextColor.GRAY);
    }

    private Component summaryText() {
        int perfect = count(HitMargin.PERFECT);
        int nearPerfect = count(HitMargin.EARLY_PERFECT) + count(HitMargin.LATE_PERFECT);
        int very = count(HitMargin.VERY_EARLY) + count(HitMargin.VERY_LATE);
        int miss = count(HitMargin.TOO_LATE);
        return Component.text()
                .append(Component.text(perfect + " ", TextColor.color(0x60FF4E)))
                .append(Component.text(nearPerfect + " ", TextColor.color(0xFCFF4E)))
                .append(Component.text(very + " ", TextColor.color(0xFF6F4E)))
                .append(Component.text(String.valueOf(miss), TextColor.color(0xFF0000)))
                .build();
    }

    private int count(HitMargin margin) {
        return counts.getOrDefault(margin, 0);
    }
}
