package io.github.bindglam.openorbit;

/** 레벨 설정 중 게임 플레이에 쓰이는 값. pitch 는 % 단위 (100 = 원래 속도). */
public record LevelSettings(double bpm, double offsetMs, double pitch, int countdownTicks, String songFilename) {
}
