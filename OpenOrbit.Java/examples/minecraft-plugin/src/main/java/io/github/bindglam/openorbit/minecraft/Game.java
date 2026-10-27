package io.github.bindglam.openorbit.minecraft;

import io.github.bindglam.openorbit.Chart;
import io.github.bindglam.openorbit.GameSession;
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
import java.util.concurrent.locks.LockSupport;
import java.util.logging.Level;

/**
 * 플레이어 한 명의 한 판. 차트를 넘겨받아 {@link #close()} 때 같이 닫는다.
 * <p>
 * 스레드:
 * <ul>
 *   <li>게임 스레드: {@link #LOOP_INTERVAL_NANOS} 마다 판정(미드스핀 자동 통과, 미스, 오토플레이)과 트레일을 처리한다.
 *       서버 틱이나 TPS 와 상관없이 곡 시각에 맞춰 돈다.</li>
 *   <li>메인 스레드: 입력({@link #press}, {@link #onInput}), 화면({@link #render}), 시작/정리.
 *       엔티티는 메인 스레드에서만 움직일 수 있다.</li>
 * </ul>
 * 세션과 판정 상태는 {@link #lock} 으로 보호한다. 사운드/액션바/타이틀/파티클은 패킷만 보내므로 게임 스레드에서 바로 보낸다.
 * 곡 시계는 System.nanoTime 기준이다.
 */
final class Game {
    enum State { LOADING, PLAYING, FINISHED }

    private static final long LOOP_INTERVAL_NANOS = TimeUnit.MILLISECONDS.toNanos(1);
    private static final long TRAIL_INTERVAL_NANOS = TimeUnit.MILLISECONDS.toNanos(15);
    /** 클리어/실패 후 결과를 보여 주다가 끝내기까지의 시간. */
    private static final long FINISH_NANOS = TimeUnit.SECONDS.toNanos(3);
    private static final Key HITSOUND = Key.key("minecraft", "block.note_block.hat");

    private final Plugin plugin;
    private final Player player;
    private final String chartId;
    private final Chart chart;
    private final GameSession session;
    private final StageRenderer stage;
    private final PluginSettings settings;
    private final boolean autoplay;
    private final double inputOffsetMs;

    private final Location returnLocation;
    private final boolean wasInvulnerable;

    private final Object lock = new Object();
    private final Map<HitMargin, Integer> counts = new EnumMap<>(HitMargin.class);
    private volatile State state = State.LOADING;
    private Key song;
    private long startNanos;
    private long finishNanos;
    private double frozenMs;
    private long nextTrailNanos;

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
        this.inputOffsetMs = inputOffsetMs;
        this.session = new GameSession(chart, settings.windows());

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

    /** 곡을 틀고 게임 스레드를 시작한다. song 이 null 이면 음악 없이 진행한다. 메인 스레드에서 부른다. */
    void begin(Key song) {
        synchronized (lock) {
            if (state != State.LOADING || closing)
                return;

            this.song = song;
            if (song != null) {
                var pitch = (float) Math.clamp(chart.settings().pitch() / 100.0, 0.5, 2.0);
                player.playSound(Sound.sound(song, Sound.Source.MASTER, 1f, pitch), Sound.Emitter.self());
            }
            startNanos = System.nanoTime();
            state = State.PLAYING;
        }
        player.sendActionBar(Component.text(chartId, NamedTextColor.GRAY));

        loop = Thread.ofPlatform()
                .name("OpenOrbit Game - " + player.getName())
                .daemon()
                .start(this::runLoop);
    }

    // ---------- 게임 스레드 ----------

    private void runLoop() {
        try {
            while (!closing && !done) {
                synchronized (lock) {
                    // close() 가 잠금을 잡고 세션을 닫는 동안 기다렸을 수 있다
                    if (closing)
                        break;
                    step(System.nanoTime());
                }
                LockSupport.parkNanos(LOOP_INTERVAL_NANOS);
            }
        } catch (RuntimeException e) {
            plugin.getLogger().log(Level.SEVERE, "게임 스레드에서 오류가 나서 종료합니다", e);
            done = true;
        }
    }

    /** lock 을 잡고 부른다. */
    private void step(long nowNanos) {
        if (state == State.FINISHED) {
            if (nowNanos - finishNanos >= FINISH_NANOS)
                done = true;
            return;
        }
        if (state != State.PLAYING)
            return;

        var now = songTimeMs(nowNanos);
        if (autoplay)
            autoplay(now);
        session.update(now, this::judge);
        checkCleared();

        if (state == State.PLAYING && nowNanos >= nextTrailNanos) {
            nextTrailNanos = nowNanos + TRAIL_INTERVAL_NANOS;
            stage.emitTrail(session.planetState(now));
        }
    }

    /** 모든 타일을 정확한 시각에 친다. */
    private void autoplay(double now) {
        while (state == State.PLAYING && !session.isFinished()) {
            var next = chart.tile(session.currentFloor() + 1);
            if (next.isMidspin() || next.timeMs() > now)
                break;
            session.update(next.timeMs(), this::judge);
            if (state == State.PLAYING && !session.isFinished() && session.currentFloor() + 1 == next.floor())
                judge(session.press(next.timeMs()));
        }
    }

    // ---------- 메인 스레드 ----------

    /** 이동 키 입력. 새로 눌린 키마다 한 번씩 친다 (얼불춤처럼 아무 키나 입력으로 받는다). */
    void onInput(Input input) {
        var pressedAt = System.nanoTime();
        var pressed = rising(lastInput.isForward(), input.isForward())
                + rising(lastInput.isBackward(), input.isBackward())
                + rising(lastInput.isLeft(), input.isLeft())
                + rising(lastInput.isRight(), input.isRight())
                + rising(lastInput.isJump(), input.isJump())
                + rising(lastInput.isSneak(), input.isSneak())
                + rising(lastInput.isSprint(), input.isSprint());
        lastInput = input;

        for (var i = 0; i < pressed; i++)
            press(pressedAt);
    }

    /** @param pressedAt 입력이 들어온 System.nanoTime. 잠금을 기다린 시간이 판정에 섞이지 않게 먼저 잰다. */
    void press(long pressedAt) {
        if (autoplay)
            return;

        synchronized (lock) {
            if (state != State.PLAYING)
                return;

            // 입력 직전까지 밀린 미드스핀/미스를 먼저 정리해야 올바른 타일을 판정한다
            var now = songTimeMs(pressedAt) - inputOffsetMs;
            session.update(now, this::judge);
            if (state != State.PLAYING)
                return;

            var hit = session.press(now);
            if (hit != null)
                judge(hit);
            checkCleared();
        }
    }

    /** 게임 스레드가 정한 상태를 엔티티에 반영한다. 매 서버 틱 메인 스레드에서 부른다. */
    void render() {
        int floor;
        PlanetState planets;
        synchronized (lock) {
            var ahead = songTimeMs(System.nanoTime()) + StageRenderer.INTERPOLATION_TICKS * 50.0;
            floor = session.currentFloor();
            planets = session.planetState(ahead);
        }
        stage.render(floor, planets);
    }

    /** 게임 스레드를 멈추고 엔티티를 지운 뒤 플레이어를 원래 자리로 돌려보낸다. */
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

        synchronized (lock) {
            stopSong();
            stage.close();
            session.close();
            chart.close();
        }
        player.setInvulnerable(wasInvulnerable);
        if (player.isOnline())
            player.teleport(returnLocation);
    }

    // ---------- 판정 (lock 을 잡고 부른다) ----------

    /** 곡 기준 시각(ms). 실제로 소리가 들리는 시점에 맞춘다. */
    private double songTimeMs(long nanos) {
        return switch (state) {
            case LOADING -> 0;
            case PLAYING -> (nanos - startNanos) / 1e6 - settings.audioLatencyMs();
            case FINISHED -> frozenMs;
        };
    }

    private void judge(HitResult result) {
        if (state != State.PLAYING)
            return;

        counts.merge(result.margin(), 1, Integer::sum);
        var margin = result.margin();

        if (margin == HitMargin.TOO_LATE && !settings.noFail()) {
            fail();
            return;
        }
        if (margin != HitMargin.TOO_EARLY && margin != HitMargin.TOO_LATE && margin != HitMargin.AUTO)
            player.playSound(Sound.sound(HITSOUND, Sound.Source.MASTER, 0.7f, 1f), Sound.Emitter.self());
        if (margin != HitMargin.AUTO)
            player.sendActionBar(judgmentText(margin).append(progressText()));
    }

    private void checkCleared() {
        if (state != State.PLAYING || !session.isFinished())
            return;
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
        var now = System.nanoTime();
        frozenMs = songTimeMs(now);
        finishNanos = now;
        state = State.FINISHED;
    }

    private void stopSong() {
        if (song != null)
            player.stopSound(SoundStop.named(song));
        song = null;
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
        var percent = 100 * session.currentFloor() / total;
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
