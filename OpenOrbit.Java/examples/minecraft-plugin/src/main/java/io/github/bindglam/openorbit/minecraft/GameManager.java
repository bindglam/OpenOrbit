package io.github.bindglam.openorbit.minecraft;

import io.github.bindglam.openorbit.Chart;
import io.github.bindglam.openorbit.OpenOrbitException;
import net.kyori.adventure.key.Key;
import net.kyori.adventure.resource.ResourcePackRequest;
import net.kyori.adventure.resource.ResourcePackStatus;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.Bukkit;
import org.bukkit.NamespacedKey;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.entity.EntityDismountEvent;
import org.bukkit.event.player.PlayerAnimationEvent;
import org.bukkit.event.player.PlayerAnimationType;
import org.bukkit.event.player.PlayerInputEvent;
import org.bukkit.event.player.PlayerQuitEvent;
import org.bukkit.persistence.PersistentDataType;
import org.bukkit.plugin.java.JavaPlugin;
import org.bukkit.scheduler.BukkitTask;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.logging.Level;

/** 플레이어별 게임을 만들고, 매 틱 돌리고, 입력을 전달한다. */
final class GameManager implements Listener {
    private final JavaPlugin plugin;
    private final PluginSettings settings;
    private final ChartLibrary charts;
    private final SongPackServer packs;
    private final NamespacedKey inputOffsetKey;

    private final Map<UUID, Game> games = new HashMap<>();
    /** 플레이어가 이미 받아서 적용한 곡 리소스팩. 다시 보내지 않는다. */
    private final Map<UUID, Set<UUID>> loadedPacks = new HashMap<>();
    private final BukkitTask task;

    GameManager(JavaPlugin plugin, PluginSettings settings, ChartLibrary charts, SongPackServer packs) {
        this.plugin = plugin;
        this.settings = settings;
        this.charts = charts;
        this.packs = packs;
        this.inputOffsetKey = new NamespacedKey(plugin, "input_offset");
        task = Bukkit.getScheduler().runTaskTimer(plugin, this::tick, 1, 1);
    }

    ChartLibrary charts() {
        return charts;
    }

    /** 채보를 비동기로 불러온 뒤 게임을 시작한다. */
    void start(Player player, String chartId, boolean autoplay) {
        var path = charts.resolve(chartId).orElse(null);
        if (path == null) {
            player.sendMessage(Component.text("채보를 찾을 수 없습니다: " + chartId, NamedTextColor.RED));
            return;
        }
        stop(player);
        player.sendMessage(Component.text("불러오는 중: " + chartId, NamedTextColor.GRAY));

        Bukkit.getScheduler().runTaskAsynchronously(plugin, () -> {
            Chart chart;
            try {
                chart = Chart.load(path);
            } catch (IOException | OpenOrbitException e) {
                Bukkit.getScheduler().runTask(plugin, () ->
                        player.sendMessage(Component.text("채보를 읽지 못했습니다: " + e.getMessage(), NamedTextColor.RED)));
                return;
            }

            SongPackServer.SongPack pack = null;
            String songProblem = null;
            var song = path.resolveSibling(chart.settings().songFilename());
            if (packs == null)
                songProblem = "리소스팩이 꺼져 있어 음악 없이 플레이합니다.";
            else if (!Files.isRegularFile(song))
                songProblem = "곡 파일이 없어 음악 없이 플레이합니다: " + chart.settings().songFilename();
            else if (!song.getFileName().toString().toLowerCase(Locale.ROOT).endsWith(".ogg"))
                songProblem = "마인크래프트는 .ogg 만 재생할 수 있어 음악 없이 플레이합니다: " + chart.settings().songFilename();
            else {
                try {
                    pack = packs.packFor(song);
                } catch (IOException e) {
                    plugin.getLogger().log(Level.WARNING, "곡 리소스팩을 만들지 못했습니다: " + song, e);
                    songProblem = "곡 리소스팩을 만들지 못해 음악 없이 플레이합니다.";
                }
            }

            var finalPack = pack;
            var finalProblem = songProblem;
            Bukkit.getScheduler().runTask(plugin, () -> {
                if (!player.isOnline()) {
                    chart.close();
                    return;
                }
                stop(player);
                var game = new Game(plugin, player, chartId, chart, settings, autoplay, inputOffset(player));
                games.put(player.getUniqueId(), game);

                if (finalProblem != null)
                    player.sendMessage(Component.text(finalProblem, NamedTextColor.YELLOW));
                sendPackThenBegin(game, finalPack);
            });
        });
    }

    void stop(Player player) {
        var game = games.remove(player.getUniqueId());
        if (game != null)
            game.close();
    }

    void stopAll() {
        task.cancel();
        games.values().forEach(Game::close);
        games.clear();
    }

    double inputOffset(Player player) {
        return player.getPersistentDataContainer().getOrDefault(inputOffsetKey, PersistentDataType.DOUBLE, settings.defaultInputOffsetMs());
    }

    void setInputOffset(Player player, double ms) {
        player.getPersistentDataContainer().set(inputOffsetKey, PersistentDataType.DOUBLE, ms);
    }

    // ---------- 곡 리소스팩 ----------

    private void sendPackThenBegin(Game game, SongPackServer.SongPack pack) {
        if (pack == null) {
            beginLater(game, null);
            return;
        }

        var player = game.player();
        var loaded = loadedPacks.computeIfAbsent(player.getUniqueId(), id -> new HashSet<>());
        if (loaded.contains(pack.info().id())) {
            beginLater(game, pack.sound());
            return;
        }

        player.sendMessage(Component.text("곡 리소스팩을 받는 중...", NamedTextColor.GRAY));
        player.sendResourcePacks(ResourcePackRequest.resourcePackRequest()
                .packs(pack.info())
                .replace(false)
                .required(false)
                .prompt(Component.text("이 채보의 곡을 재생하려면 리소스팩이 필요합니다."))
                .callback((id, status, audience) -> {
                    if (status.intermediate())
                        return;
                    // 콜백이 어느 스레드에서 올지 모르므로 메인 스레드로 넘긴다
                    Bukkit.getScheduler().runTask(plugin, () -> onPackStatus(game, pack, status));
                }));
    }

    private void onPackStatus(Game game, SongPackServer.SongPack pack, ResourcePackStatus status) {
        if (games.get(game.player().getUniqueId()) != game)
            return;

        if (status == ResourcePackStatus.SUCCESSFULLY_LOADED) {
            loadedPacks.computeIfAbsent(game.player().getUniqueId(), id -> new HashSet<>()).add(pack.info().id());
            beginLater(game, pack.sound());
        } else {
            game.player().sendMessage(Component.text("리소스팩을 적용하지 못해 음악 없이 플레이합니다 (" + status + ").", NamedTextColor.YELLOW));
            beginLater(game, null);
        }
    }

    /** 화면이 자리 잡을 시간을 조금 준 뒤 시작한다. */
    private void beginLater(Game game, Key song) {
        Bukkit.getScheduler().runTaskLater(plugin, () -> {
            if (games.get(game.player().getUniqueId()) == game)
                game.begin(song);
        }, 20);
    }

    // ---------- 틱 ----------

    /** 판정은 각 게임의 스레드가 하고, 여기서는 끝난 게임을 정리하고 화면만 갱신한다. */
    private void tick() {
        var it = games.values().iterator();
        while (it.hasNext()) {
            var game = it.next();
            var alive = !game.isDone();
            if (alive) {
                try {
                    game.render();
                } catch (RuntimeException e) {
                    plugin.getLogger().log(Level.SEVERE, "게임 화면을 그리다 오류가 나서 종료합니다", e);
                    alive = false;
                }
            }
            if (!alive) {
                it.remove();
                game.close();
            }
        }
    }

    // ---------- 이벤트 ----------

    @EventHandler(priority = EventPriority.MONITOR)
    private void onInput(PlayerInputEvent event) {
        var game = games.get(event.getPlayer().getUniqueId());
        if (game != null)
            game.onInput(event.getInput());
    }

    /** 좌클릭(팔 휘두르기)도 입력으로 받는다. */
    @EventHandler(priority = EventPriority.MONITOR)
    private void onSwing(PlayerAnimationEvent event) {
        if (event.getAnimationType() != PlayerAnimationType.ARM_SWING)
            return;
        var game = games.get(event.getPlayer().getUniqueId());
        if (game != null)
            game.press(System.nanoTime());
    }

    /** 쉬프트도 입력이므로 플레이 중에는 카메라에서 내리지 않는다. */
    @EventHandler
    private void onDismount(EntityDismountEvent event) {
        if (!(event.getEntity() instanceof Player player))
            return;
        var game = games.get(player.getUniqueId());
        if (game != null && game.isCamera(event.getDismounted()))
            event.setCancelled(true);
    }

    @EventHandler
    private void onQuit(PlayerQuitEvent event) {
        stop(event.getPlayer());
        loadedPacks.remove(event.getPlayer().getUniqueId());
    }

    Path chartsRoot() {
        return charts.root();
    }
}
