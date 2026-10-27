package io.github.bindglam.openorbit.minecraft;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Optional;
import java.util.stream.Stream;

/**
 * plugins/OpenOrbit/charts 아래의 .adofai 파일 목록.
 * 채보 ID 는 charts 폴더 기준 상대 경로에서 확장자를 뺀 것 (예: "Kplecraft - 16383/16383").
 * 곡 파일은 채보와 같은 폴더에서 찾는다.
 */
final class ChartLibrary {
    private static final String EXTENSION = ".adofai";

    private final Path root;

    ChartLibrary(Path root) throws IOException {
        this.root = root.toAbsolutePath().normalize();
        Files.createDirectories(this.root);
    }

    Path root() {
        return root;
    }

    List<String> ids() {
        try (Stream<Path> files = Files.walk(root, 4)) {
            return files
                    .filter(p -> Files.isRegularFile(p) && p.getFileName().toString().endsWith(EXTENSION))
                    .map(this::idOf)
                    .sorted(String.CASE_INSENSITIVE_ORDER)
                    .toList();
        } catch (IOException e) {
            return List.of();
        }
    }

    /** ID 에 해당하는 .adofai 경로. charts 폴더 밖을 가리키면 비어 있다. */
    Optional<Path> resolve(String id) {
        var path = root.resolve(id + EXTENSION).normalize();
        if (!path.startsWith(root) || !Files.isRegularFile(path))
            return Optional.empty();
        return Optional.of(path);
    }

    private String idOf(Path file) {
        var relative = root.relativize(file).toString().replace('\\', '/');
        return relative.substring(0, relative.length() - EXTENSION.length());
    }
}
