package io.github.bindglam.openorbit.minecraft;

import io.papermc.paper.plugin.lifecycle.event.types.LifecycleEvents;
import org.bukkit.plugin.java.JavaPlugin;

import java.io.IOException;
import java.util.List;
import java.util.logging.Level;

/** 마인크래프트에서 얼불춤 채보를 플레이하는 OpenOrbit 예제 플러그인. */
public final class OpenOrbitPlugin extends JavaPlugin {
    private SongPackServer packs;
    private GameManager games;

    @Override
    public void onEnable() {
        saveDefaultConfig();
        var settings = PluginSettings.load(getConfig());

        ChartLibrary charts;
        try {
            charts = new ChartLibrary(getDataFolder().toPath().resolve("charts"));
            if (settings.resourcePackEnabled())
                packs = new SongPackServer(settings.resourcePackPort(), settings.resourcePackPublicUrl());
        } catch (IOException e) {
            getLogger().log(Level.SEVERE, "초기화하지 못했습니다", e);
            getServer().getPluginManager().disablePlugin(this);
            return;
        }

        games = new GameManager(this, settings, charts, packs);
        getServer().getPluginManager().registerEvents(games, this);

        getLifecycleManager().registerEventHandler(LifecycleEvents.COMMANDS, event ->
                event.registrar().register(AdofaiCommand.create(games), "얼불춤 채보를 플레이합니다", List.of("openorbit")));
    }

    @Override
    public void onDisable() {
        if (games != null)
            games.stopAll();
        if (packs != null)
            packs.close();
    }
}
