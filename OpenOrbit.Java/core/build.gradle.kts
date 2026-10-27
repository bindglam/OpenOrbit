plugins {
    `java-library`
}

group = "io.github.bindglam.openorbit"
version = "0.1.0"

base {
    archivesName = "openorbit-core"
}

java {
    toolchain {
        languageVersion = JavaLanguageVersion.of(25)
    }
    withSourcesJar()
}

repositories {
    mavenCentral()
}

// OpenOrbit.Native 를 NativeAOT 로 퍼블리시한 결과(../OpenOrbit.Native/bin/publish/<rid>/)를
// jar 안의 natives/<rid>/ 로 넣는다. 들어 있는 RID 만 지원된다.
val nativePublishDir = rootDir.resolve("../OpenOrbit.Native/bin/publish")

tasks.processResources {
    from(nativePublishDir) {
        include("*/openorbit.dylib", "*/openorbit.so", "*/openorbit.dll")
        into("natives")
    }
}

/** 지금 OS/아키텍처용 네이티브 라이브러리를 퍼블리시한다. 다른 OS 용은 그 OS 에서 빌드해야 한다. */
val publishNative by tasks.registering(Exec::class) {
    val os = System.getProperty("os.name").lowercase()
    val arch = System.getProperty("os.arch").lowercase()
    val rid = (when {
        "mac" in os -> "osx"
        "win" in os -> "win"
        else -> "linux"
    }) + "-" + (if (arch == "aarch64" || arch == "arm64") "arm64" else "x64")

    workingDir = rootDir.resolve("..")
    commandLine(
        "dotnet", "publish", "OpenOrbit.Native", "-c", "Release", "-r", rid,
        "-o", nativePublishDir.resolve(rid).path,
    )
}

tasks.withType<JavaCompile> {
    // FFM 의 restricted 메서드 경고는 의도된 것
    options.compilerArgs.add("-Xlint:-restricted")
}
