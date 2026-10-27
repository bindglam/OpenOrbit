package io.github.bindglam.openorbit.minecraft;

import io.github.bindglam.openorbit.JudgmentWindows;
import org.bukkit.configuration.file.FileConfiguration;

import java.util.Locale;

/** config.yml 값. */
record PluginSettings(
        JudgmentWindows windows,
        boolean noFail,
        double scale,
        double cameraHeight,
        int stageHeight,
        int renderAhead,
        int renderBehind,
        double audioLatencyMs,
        double defaultInputOffsetMs,
        boolean resourcePackEnabled,
        int resourcePackPort,
        String resourcePackPublicUrl) {

    static PluginSettings load(FileConfiguration config) {
        var windows = switch (config.getString("judgment", "lenient").toLowerCase(Locale.ROOT)) {
            case "normal" -> JudgmentWindows.NORMAL;
            case "strict" -> JudgmentWindows.STRICT;
            case "super_strict" -> JudgmentWindows.SUPER_STRICT;
            default -> JudgmentWindows.LENIENT;
        };

        return new PluginSettings(
                windows,
                config.getBoolean("no-fail", false),
                config.getDouble("scale", 2.0),
                config.getDouble("camera-height", 14.0),
                config.getInt("stage-height", 40),
                config.getInt("render-ahead", 32),
                config.getInt("render-behind", 8),
                config.getDouble("audio-latency-ms", 0),
                config.getDouble("default-input-offset-ms", 50),
                config.getBoolean("resource-pack.enabled", true),
                config.getInt("resource-pack.port", 8765),
                config.getString("resource-pack.public-url", "http://localhost:8765"));
    }
}
