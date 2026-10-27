package io.github.bindglam.openorbit.minecraft;

import io.github.bindglam.openorbit.Chart;
import io.github.bindglam.openorbit.PlanetState;
import io.github.bindglam.openorbit.Vec2;
import org.bukkit.Color;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.Particle;
import org.bukkit.block.data.BlockData;
import org.bukkit.entity.BlockDisplay;
import org.bukkit.entity.Display;
import org.bukkit.entity.Entity;
import org.bukkit.entity.ItemDisplay;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
import org.bukkit.plugin.Plugin;
import org.bukkit.util.Transformation;
import org.joml.Quaternionf;
import org.joml.Vector3f;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;

/**
 * 채보를 디스플레이 엔티티로 그린다. 모든 엔티티는 플레이어 한 명에게만 보인다.
 * <p>
 * 좌표: 코어의 (x, y) → 월드 (origin.x + x * scale, origin.y, origin.z - y * scale).
 * 위에서 북쪽(yaw 180)을 보고 내려다보면 코어의 +Y 가 화면 위, +X 가 화면 오른쪽이 된다.
 * <p>
 * {@link #emitTrail} 만 아무 스레드에서나 불러도 되고, 나머지는 메인 스레드 전용이다.
 */
final class StageRenderer {
    private static final BlockData TILE = Material.WHITE_CONCRETE.createBlockData();
    private static final BlockData TILE_PASSED = Material.LIGHT_GRAY_CONCRETE.createBlockData();
    private static final BlockData OUTLINE = Material.BLACK_CONCRETE.createBlockData();
    /** 다음에 칠 타일은 테두리 색으로 표시한다. */
    private static final BlockData OUTLINE_NEXT = Material.YELLOW_CONCRETE.createBlockData();
    private static final Display.Brightness BRIGHT = new Display.Brightness(15, 15);
    /** 지나간 타일의 이벤트 아이콘은 어둡게 한다. */
    private static final Display.Brightness DIM = new Display.Brightness(4, 4);

    /** 행성 번호별 블록과 트레일 색. 0 = 불, 1 = 얼음, 2 = 3공, 3 = 4공 ... (얼불춤 기본 색에 맞춤) */
    private record PlanetLook(BlockData block, Particle.DustOptions trail) {
        PlanetLook(Material block, int rgb) {
            this(block.createBlockData(), new Particle.DustOptions(Color.fromRGB(rgb), 1.4f));
        }
    }

    private static final PlanetLook[] PLANETS = {
            new PlanetLook(Material.RED_CONCRETE, 0xE0402F),
            new PlanetLook(Material.LIGHT_BLUE_CONCRETE, 0x3AAFD9),
            new PlanetLook(Material.LIME_CONCRETE, 0x5EC22A),
            new PlanetLook(Material.PURPLE_CONCRETE, 0x8A35B5),
            new PlanetLook(Material.YELLOW_CONCRETE, 0xF2C41B),
            new PlanetLook(Material.ORANGE_CONCRETE, 0xF07613),
            new PlanetLook(Material.PINK_CONCRETE, 0xEE8DAC),
            new PlanetLook(Material.CYAN_CONCRETE, 0x15899A),
    };

    /** 길 위 이벤트 표시. 얼불춤 아이콘(소용돌이, 토끼, 달팽이)에 가까운 아이템. */
    private static final Material TWIRL = Material.ENDER_EYE;
    private static final Material SPEED_UP = Material.RABBIT_FOOT;
    private static final Material SLOW_DOWN = Material.TURTLE_SCUTE;

    /** 타일 크기, 행성 크기, 이벤트 아이콘 크기 (타일 한 칸 = 1 기준). Godot 예제의 TrackView 와 같은 비율. */
    private static final float TILE_WIDTH = 0.5f;
    /** 팔 길이. 0.5 보다 짧게 그려 타일 사이에 틈을 두고, 그 틈을 테두리가 채워 경계선이 된다. */
    private static final float ARM_LENGTH = 0.46f;
    private static final float OUTLINE_WIDTH = 0.04f;
    private static final float TILE_THICKNESS = 0.06f;
    private static final float PLANET_SIZE = 0.38f;
    private static final float MARKER_SIZE = 0.4f;

    /** 엔티티 위치 보간 시간(틱). 목표 위치를 그만큼 앞선 시각으로 잡아 화면이 늦지 않게 한다. */
    static final int INTERPOLATION_TICKS = 0;
    /** 카메라가 중심 행성을 따라가는 정도 (틱당). */
    private static final double CAMERA_FOLLOW = 0.18;

    private final Plugin plugin;
    private final Player viewer;
    private final Chart chart;
    private final Location origin;
    private final double scale;
    private final double cameraHeight;
    private final int renderAhead;
    private final int renderBehind;

    private enum TileLook { UPCOMING, NEXT, PASSED }

    /** 한 자리에 그리는 엔티티들. */
    private static final class TileView {
        final List<BlockDisplay> fills = new ArrayList<>();
        final List<BlockDisplay> outlines = new ArrayList<>();
        final List<ItemDisplay> markers = new ArrayList<>();
        TileLook look;

        void remove() {
            fills.forEach(Entity::remove);
            outlines.forEach(Entity::remove);
            markers.forEach(Entity::remove);
        }
    }

    /**
     * 타일 번호 → 같은 자리에 있는 타일 묶음의 대표(가장 앞 번호). 미드스핀처럼 같은 자리에 타일이 겹치면
     * 따로 그릴 때 면이 겹쳐 깜빡이므로 대표 하나만 그리고, 묶음 전체의 앞뒤 방향으로 팔을 뻗는다.
     */
    private final int[] owners;
    /** 대표 → 묶음에 속한 타일들 (혼자인 타일은 없음). */
    private final Map<Integer, List<Integer>> groups = new HashMap<>();
    /** 대표 번호 → 그려 둔 엔티티. */
    private final Map<Integer, TileView> tiles = new HashMap<>();
    private final List<BlockDisplay> planets = new ArrayList<>();
    private final ItemDisplay camera;
    private double cameraX;
    private double cameraY;

    StageRenderer(Plugin plugin, Player viewer, Chart chart, Location origin, PluginSettings settings) {
        this.plugin = plugin;
        this.viewer = viewer;
        this.chart = chart;
        this.origin = origin.clone();
        this.scale = settings.scale();
        this.cameraHeight = settings.cameraHeight();
        this.renderAhead = settings.renderAhead();
        this.renderBehind = settings.renderBehind();

        var list = chart.tiles();
        owners = new int[list.size()];
        for (var i = 0; i < owners.length; i++) {
            owners[i] = i;
            // 같은 자리 타일은 바로 앞 몇 칸 안에서만 생긴다 (미드스핀)
            for (var j = i - 1; j >= Math.max(0, i - 4); j--) {
                if (samePosition(list.get(i).position(), list.get(j).position())) {
                    owners[i] = owners[j];
                    groups.computeIfAbsent(owners[j], o -> new ArrayList<>(List.of(o))).add(i);
                    break;
                }
            }
        }

        // 플레이어가 타는 보이지 않는 카메라. 탈것이 보여야 클라이언트도 탄 것으로 처리하므로 그 플레이어에게는 보여 준다.
        camera = spawn(cameraLocation(), ItemDisplay.class, e -> e.setTeleportDuration(INTERPOLATION_TICKS));
    }

    /** 플레이어를 카메라에 태우고 아래를 보게 한다. */
    void mount() {
        var look = cameraLocation();
        look.setYaw(180);
        look.setPitch(90);
        viewer.leaveVehicle();
        viewer.teleport(look);
        camera.addPassenger(viewer);
    }

    boolean isCamera(Entity entity) {
        return camera.equals(entity);
    }

    /**
     * @param currentFloor 중심 행성이 서 있는 타일.
     * @param planetState  보간 시간만큼 앞선 시각의 행성 상태.
     */
    void render(int currentFloor, PlanetState planetState) {
        renderTiles(currentFloor);
        renderPlanets(planetState);
        followCamera(planetState.pivotPosition());
    }

    /**
     * 회전 중인 행성들 자리에 파티클을 뿌린다. 파티클은 패킷만 보내므로 게임 스레드에서 서버 틱보다 촘촘하게 불러도 된다.
     * 디스플레이는 보간 시간만큼 앞선 위치로 보내 두므로, 여기에는 지금 시각의 상태를 넘겨야 행성과 겹친다.
     */
    void emitTrail(PlanetState state) {
        var height = PLANET_SIZE * scale / 2;
        for (var k = 1; k < state.planets(); k++) {
            var p = state.positionAt(k);
            var look = PLANETS[state.planetAt(k) % PLANETS.length];
            viewer.spawnParticle(Particle.DUST,
                    origin.getX() + p.x() * scale, origin.getY() + height, origin.getZ() - p.y() * scale,
                    1, 0, 0, 0, 0, look.trail());
        }
    }

    void close() {
        camera.eject();
        camera.remove();
        tiles.values().forEach(TileView::remove);
        tiles.clear();
        planets.forEach(Entity::remove);
        planets.clear();
    }

    // ---------- 타일 ----------

    private void renderTiles(int currentFloor) {
        var from = Math.max(0, currentFloor - renderBehind);
        var to = Math.min(owners.length - 1, currentFloor + renderAhead);

        var visible = new HashSet<Integer>();
        for (var floor = from; floor <= to; floor++)
            visible.add(owners[floor]);

        tiles.entrySet().removeIf(entry -> {
            if (visible.contains(entry.getKey()))
                return false;
            entry.getValue().remove();
            return true;
        });

        for (var owner : visible) {
            var look = lookOf(owner, currentFloor);
            var view = tiles.get(owner);
            if (view == null) {
                view = spawnTile(owner);
                tiles.put(owner, view);
            } else if (view.look == look) {
                continue;
            }
            applyLook(view, look);
        }
    }

    private TileLook lookOf(int owner, int currentFloor) {
        var members = members(owner);
        if (members.contains(currentFloor + 1))
            return TileLook.NEXT;
        return members.getLast() <= currentFloor ? TileLook.PASSED : TileLook.UPCOMING;
    }

    private static void applyLook(TileView view, TileLook look) {
        var passed = look == TileLook.PASSED;
        for (var fill : view.fills)
            fill.setBlock(passed ? TILE_PASSED : TILE);
        for (var outline : view.outlines)
            outline.setBlock(look == TileLook.NEXT ? OUTLINE_NEXT : OUTLINE);
        for (var marker : view.markers)
            marker.setBrightness(passed ? DIM : BRIGHT);
        view.look = look;
    }

    private List<Integer> members(int owner) {
        var group = groups.get(owner);
        return group != null ? group : List.of(owner);
    }

    /**
     * 얼불춤 타일처럼, 가운데 네모에서 앞/뒤 타일 쪽으로 팔을 뻗는다. 검은 테두리를 먼저 깔고 그 위에 채움을 올린다.
     * 미드스핀 직전 타일은 앞뒤가 같은 방향이라 팔이 하나만 남아 막다른 길로 보인다.
     */
    private TileView spawnTile(int owner) {
        var tiles = chart.tiles();
        var center = tiles.get(owner).position();
        var members = members(owner);

        var angles = new ArrayList<Double>(4);
        for (var member : members) {
            for (var neighbor : new int[]{member - 1, member + 1}) {
                if (neighbor < 0 || neighbor >= tiles.size() || owners[neighbor] == owner)
                    continue;
                var p = tiles.get(neighbor).position();
                if (samePosition(p, center))
                    continue;
                var angle = Math.atan2(p.y() - center.y(), p.x() - center.x());
                if (angles.stream().noneMatch(a -> Math.abs(Math.IEEEremainder(a - angle, 2 * Math.PI)) < 1e-3))
                    angles.add(angle);
            }
        }
        // 첫/마지막 타일은 팔이 하나뿐이라 반대쪽으로도 하나 뻗어 일반 타일과 모양을 맞춘다
        if (angles.size() == 1 && (owner == 0 || members.getLast() == tiles.size() - 1))
            angles.add(angles.getFirst() + Math.PI);
        if (angles.isEmpty())
            angles.add(0.0);

        var view = new TileView();
        var location = toWorld(center, 0);
        var b = OUTLINE_WIDTH;
        for (var angle : angles) {
            // 테두리는 채움보다 사방으로 b 만큼 크고, 위에서 봤을 때 가려지도록 살짝 아래에 둔다
            view.outlines.add(spawnArm(location, angle, -TILE_WIDTH / 2 - b, ARM_LENGTH + b, TILE_WIDTH + 2 * b, 0.01f, OUTLINE));
            view.fills.add(spawnArm(location, angle, -TILE_WIDTH / 2, ARM_LENGTH, TILE_WIDTH, 0, TILE));
        }
        for (var member : members)
            spawnMarkers(member, view.markers);
        return view;
    }

    /**
     * 방향 angle(라디안) 으로 [start, end] 구간을 덮는 판. 위쪽 면이 y = -drop 이 된다. 값은 모두 타일 한 칸 = 1 기준.
     */
    private BlockDisplay spawnArm(Location location, double angle, float start, float end, float width, float drop, BlockData block) {
        var s = (float) scale;
        var thickness = TILE_THICKNESS * s;

        // 블록 모델 (0..1)^3 을 (start..end, 아래로 thickness, -width/2..width/2) 로 늘린 뒤 진행 방향으로 돌린다
        var rotation = new Quaternionf().rotateY((float) angle);
        var translation = rotation.transform(new Vector3f(start * s, -thickness - drop * s, -width / 2 * s));
        var transformation = new Transformation(
                translation, rotation, new Vector3f((end - start) * s, thickness, width * s), new Quaternionf());

        return spawn(location, BlockDisplay.class, e -> {
            e.setBlock(block);
            e.setTransformation(transformation);
        });
    }

    /**
     * 이 타일에서 회전 방향이나 BPM 이 바뀌면 타일 위에 아이템을 눕혀 표시한다.
     * Tile 의 clockwise/bpm 은 "이 타일을 떠날 때" 값이므로 앞 타일과 비교하면 이 타일의 Twirl/SetSpeed 가 된다.
     */
    private void spawnMarkers(int floor, List<ItemDisplay> out) {
        var tiles = chart.tiles();
        var tile = tiles.get(floor);
        var previousBpm = floor == 0 ? chart.settings().bpm() : tiles.get(floor - 1).bpm();
        var previousClockwise = floor == 0 || tiles.get(floor - 1).clockwise();

        var markers = new ArrayList<Material>(2);
        if (tile.clockwise() != previousClockwise)
            markers.add(TWIRL);
        if (tile.bpm() > previousBpm + 1e-6)
            markers.add(SPEED_UP);
        else if (tile.bpm() < previousBpm - 1e-6)
            markers.add(SLOW_DOWN);

        var size = (float) (MARKER_SIZE * scale);
        // 바닥에 눕힌다 (앞면이 위, 아이템의 위쪽이 화면 위쪽(북쪽))
        var transformation = new Transformation(
                new Vector3f(0, 0.02f, 0), new Quaternionf().rotateX((float) (-Math.PI / 2)),
                new Vector3f(size, size, size), new Quaternionf());

        for (var i = 0; i < markers.size(); i++) {
            // 둘 다 있으면 화면 가로로 나란히 놓는다
            var offset = (i - (markers.size() - 1) / 2.0) * MARKER_SIZE;
            var position = new Vec2(tile.position().x() + offset, tile.position().y());
            var item = new ItemStack(markers.get(i));
            out.add(spawn(toWorld(position, 0), ItemDisplay.class, e -> {
                e.setItemStack(item);
                e.setItemDisplayTransform(ItemDisplay.ItemDisplayTransform.FIXED);
                e.setTransformation(transformation);
            }));
        }
    }

    private static boolean samePosition(Vec2 a, Vec2 b) {
        return Math.abs(a.x() - b.x()) < 1e-6 && Math.abs(a.y() - b.y()) < 1e-6;
    }

    // ---------- 행성 ----------

    private void renderPlanets(PlanetState state) {
        var count = state.planets();
        while (planets.size() > count)
            planets.removeLast().remove();

        var size = (float) (PLANET_SIZE * scale);
        var transformation = new Transformation(
                new Vector3f(-size / 2, 0, -size / 2), new Quaternionf(), new Vector3f(size, size, size), new Quaternionf());

        for (var k = 0; k < count; k++) {
            var planet = state.planetAt(k);
            var location = toWorld(state.positionAt(k), 0);

            if (planet < planets.size()) {
                planets.get(planet).teleport(location);
                continue;
            }
            // 행성 번호 순서대로 만든다 (planet == planets.size() 가 될 때까지 앞 번호부터)
            while (planets.size() <= planet) {
                var block = PLANETS[planets.size() % PLANETS.length].block();
                planets.add(spawn(location, BlockDisplay.class, e -> {
                    e.setBlock(block);
                    e.setTransformation(transformation);
                    e.setTeleportDuration(INTERPOLATION_TICKS);
                }));
            }
        }
    }

    // ---------- 카메라 ----------

    private void followCamera(Vec2 target) {
        cameraX += (target.x() - cameraX) * CAMERA_FOLLOW;
        cameraY += (target.y() - cameraY) * CAMERA_FOLLOW;
        camera.teleport(cameraLocation()); // 1.21.10 부터 탑승자를 유지한 채 이동하는 게 기본 동작
    }

    private Location cameraLocation() {
        return toWorld(new Vec2(cameraX, cameraY), cameraHeight);
    }

    // ---------- 공통 ----------

    private Location toWorld(Vec2 p, double height) {
        return new Location(origin.getWorld(), origin.getX() + p.x() * scale, origin.getY() + height, origin.getZ() - p.y() * scale);
    }

    private <T extends Display> T spawn(Location location, Class<T> type, java.util.function.Consumer<T> configure) {
        var entity = location.getWorld().spawn(location, type, e -> {
            e.setPersistent(false);
            e.setVisibleByDefault(false);
            e.setBrightness(BRIGHT);
            e.setViewRange(4f);
            configure.accept(e);
        });
        viewer.showEntity(plugin, entity);
        return entity;
    }
}
