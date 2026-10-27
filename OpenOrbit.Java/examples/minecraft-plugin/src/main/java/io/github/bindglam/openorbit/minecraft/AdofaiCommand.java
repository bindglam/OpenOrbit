package io.github.bindglam.openorbit.minecraft;

import com.mojang.brigadier.arguments.DoubleArgumentType;
import com.mojang.brigadier.arguments.StringArgumentType;
import com.mojang.brigadier.context.CommandContext;
import com.mojang.brigadier.suggestion.SuggestionProvider;
import com.mojang.brigadier.tree.LiteralCommandNode;
import io.papermc.paper.command.brigadier.CommandSourceStack;
import io.papermc.paper.command.brigadier.Commands;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.entity.Player;

import java.util.Locale;

/**
 * /adofai play &lt;채보&gt;, /adofai auto &lt;채보&gt;, /adofai stop, /adofai list, /adofai offset [ms]
 */
final class AdofaiCommand {
    private AdofaiCommand() {}

    static LiteralCommandNode<CommandSourceStack> create(GameManager games) {
        SuggestionProvider<CommandSourceStack> chartSuggestions = (ctx, builder) -> {
            var typed = builder.getRemainingLowerCase();
            for (var id : games.charts().ids())
                if (id.toLowerCase(Locale.ROOT).contains(typed))
                    builder.suggest(id);
            return builder.buildFuture();
        };

        return Commands.literal("adofai")
                .then(Commands.literal("play")
                        .then(Commands.argument("chart", StringArgumentType.greedyString())
                                .suggests(chartSuggestions)
                                .executes(ctx -> play(ctx, games, false))))
                .then(Commands.literal("auto")
                        .then(Commands.argument("chart", StringArgumentType.greedyString())
                                .suggests(chartSuggestions)
                                .executes(ctx -> play(ctx, games, true))))
                .then(Commands.literal("stop")
                        .executes(ctx -> {
                            if (player(ctx) instanceof Player player)
                                games.stop(player);
                            return 1;
                        }))
                .then(Commands.literal("list")
                        .executes(ctx -> {
                            var ids = games.charts().ids();
                            var sender = ctx.getSource().getSender();
                            if (ids.isEmpty()) {
                                sender.sendMessage(Component.text(
                                        "채보가 없습니다. " + games.chartsRoot() + " 에 채보 폴더를 넣어 주세요.", NamedTextColor.YELLOW));
                            }
                            ids.forEach(id -> sender.sendMessage(Component.text("- " + id)));
                            return ids.size();
                        }))
                .then(Commands.literal("offset")
                        .executes(ctx -> {
                            if (player(ctx) instanceof Player player)
                                player.sendMessage(Component.text("입력 오프셋: " + games.inputOffset(player) + "ms"));
                            return 1;
                        })
                        .then(Commands.argument("ms", DoubleArgumentType.doubleArg(-500, 500))
                                .executes(ctx -> {
                                    if (player(ctx) instanceof Player player) {
                                        var ms = DoubleArgumentType.getDouble(ctx, "ms");
                                        games.setInputOffset(player, ms);
                                        player.sendMessage(Component.text("입력 오프셋을 " + ms + "ms 로 바꿨습니다. 다음 판부터 적용됩니다."));
                                    }
                                    return 1;
                                })))
                .build();
    }

    private static int play(CommandContext<CommandSourceStack> ctx, GameManager games, boolean autoplay) {
        if (player(ctx) instanceof Player player)
            games.start(player, StringArgumentType.getString(ctx, "chart"), autoplay);
        return 1;
    }

    private static Player player(CommandContext<CommandSourceStack> ctx) {
        if (ctx.getSource().getExecutor() instanceof Player player)
            return player;
        ctx.getSource().getSender().sendMessage(Component.text("플레이어만 쓸 수 있습니다.", NamedTextColor.RED));
        return null;
    }
}
