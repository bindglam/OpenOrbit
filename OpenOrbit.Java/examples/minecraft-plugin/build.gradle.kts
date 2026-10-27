plugins {
    java
    id("com.gradleup.shadow") version "9.6.1"
    id("xyz.jpenilla.resource-factory-paper-convention") version "1.4.0"
    id("xyz.jpenilla.run-paper") version "3.1.0"
}

group = "io.github.bindglam.openorbit.minecraft"
version = "0.1.0"

val minecraftVersion = "26.3"

repositories {
    mavenCentral()
    maven("https://repo.papermc.io/repository/maven-public/")
}

dependencies {
    compileOnly("io.papermc.paper:paper-api:$minecraftVersion.build.+")

    // 네이티브 라이브러리(natives/<rid>/)까지 플러그인 jar 에 같이 들어간다
    implementation(project(":core"))
}

java {
    toolchain.languageVersion.set(JavaLanguageVersion.of(25))
}

paperPluginYaml {
    name = "OpenOrbit"
    main = "$group.OpenOrbitPlugin"
    authors.add("bindglam")
    apiVersion = "26.3"
    description = "A Dance of Fire and Ice in Minecraft, powered by OpenOrbit"
}

tasks {
    runServer {
        minecraftVersion(minecraftVersion)
        // FFM 으로 네이티브 라이브러리를 부르므로 경고 없이 허용한다
        jvmArgs("--enable-native-access=ALL-UNNAMED")
    }

    jar {
        finalizedBy(shadowJar)
    }

    shadowJar {
        archiveBaseName = "OpenOrbit"
        archiveClassifier = ""
    }
}
