package io.github.bindglam.openorbit.minecraft;

import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import net.kyori.adventure.key.Key;
import net.kyori.adventure.resource.ResourcePackInfo;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

/**
 * 곡 파일(.ogg)마다 그 곡 하나만 사운드로 등록한 리소스팩을 만들어 내장 HTTP 서버로 내려준다.
 * 바닐라 클라이언트는 임의의 오디오를 재생할 수 없어서 리소스팩의 커스텀 사운드로 재생한다.
 */
final class SongPackServer implements AutoCloseable {
    /** 26.1 리소스팩 포맷. 그보다 새 버전도 받아들이도록 max 를 넉넉히 둔다. */
    private static final int MIN_PACK_FORMAT = 84;
    private static final int MAX_PACK_FORMAT = 1000;

    record SongPack(ResourcePackInfo info, Key sound) {}

    private record Served(byte[] zip) {}

    private final String publicUrl;
    private final HttpServer server;
    private final ExecutorService executor = Executors.newVirtualThreadPerTaskExecutor();
    private final Map<Path, SongPack> packs = new ConcurrentHashMap<>();
    private final Map<UUID, Served> served = new ConcurrentHashMap<>();

    SongPackServer(int port, String publicUrl) throws IOException {
        this.publicUrl = publicUrl.replaceAll("/+$", "");
        server = HttpServer.create(new InetSocketAddress(port), 0);
        server.createContext("/", this::handle);
        server.setExecutor(executor);
        server.start();
    }

    /** 곡 파일의 리소스팩. 처음이면 만든다 (곡을 읽고 압축하므로 메인 스레드에서 부르지 말 것). */
    SongPack packFor(Path song) throws IOException {
        var existing = packs.get(song);
        if (existing != null)
            return existing;

        var audio = Files.readAllBytes(song);
        var name = sha1(audio).substring(0, 16);
        var sound = Key.key("openorbit", "song." + name);
        var zip = buildZip(name, audio);
        var hash = sha1(zip);
        var id = UUID.nameUUIDFromBytes(hash.getBytes(StandardCharsets.US_ASCII));

        served.put(id, new Served(zip));
        var pack = new SongPack(ResourcePackInfo.resourcePackInfo(id, URI.create(publicUrl + "/" + id + ".zip"), hash), sound);
        packs.put(song, pack);
        return pack;
    }

    @Override
    public void close() {
        server.stop(0);
        executor.close();
    }

    private void handle(HttpExchange exchange) throws IOException {
        try (exchange) {
            var path = exchange.getRequestURI().getPath();
            Served pack = null;
            if (path.endsWith(".zip")) {
                try {
                    pack = served.get(UUID.fromString(path.substring(1, path.length() - 4)));
                } catch (IllegalArgumentException ignored) {
                }
            }

            if (pack == null) {
                exchange.sendResponseHeaders(404, -1);
                return;
            }
            exchange.getResponseHeaders().set("Content-Type", "application/zip");
            exchange.sendResponseHeaders(200, pack.zip().length);
            exchange.getResponseBody().write(pack.zip());
        }
    }

    private static byte[] buildZip(String name, byte[] audio) throws IOException {
        var mcmeta = """
                {"pack":{"description":"OpenOrbit song","min_format":%d,"max_format":%d}}
                """.formatted(MIN_PACK_FORMAT, MAX_PACK_FORMAT);
        // 긴 곡은 stream 으로 재생해야 메모리에 통째로 올리지 않는다
        var soundsJson = """
                {"song.%s":{"sounds":[{"name":"openorbit:song/%s","stream":true}]}}
                """.formatted(name, name);

        var bytes = new ByteArrayOutputStream(audio.length + 1024);
        try (var zip = new ZipOutputStream(bytes)) {
            put(zip, "pack.mcmeta", mcmeta.getBytes(StandardCharsets.UTF_8));
            put(zip, "assets/openorbit/sounds.json", soundsJson.getBytes(StandardCharsets.UTF_8));
            put(zip, "assets/openorbit/sounds/song/" + name + ".ogg", audio);
        }
        return bytes.toByteArray();
    }

    private static void put(ZipOutputStream zip, String name, byte[] data) throws IOException {
        var entry = new ZipEntry(name);
        entry.setTime(0); // 같은 곡이면 같은 해시가 나오도록 고정
        zip.putNextEntry(entry);
        zip.write(data);
        zip.closeEntry();
    }

    private static String sha1(byte[] data) {
        try {
            return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-1").digest(data));
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
