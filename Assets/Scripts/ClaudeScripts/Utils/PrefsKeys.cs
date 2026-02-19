/// <summary>
/// PlayerPrefs 키 상수 모음
/// 하드코딩된 문자열 대신 이 상수를 사용하여 오타 방지 및 일관성 유지
/// </summary>
public static class PrefsKeys
{
    // Auth / Login
    public const string DeviceSN = "DEVICE_SN";
    public const string LoginUsername = "LOGIN_USERNAME";
    public const string LoginUserID = "LOGIN_USERID";

    // Settings
    public const string MasterVolume = "MasterVolume";
    public const string BGMVolume = "BGMVolume";
    public const string SFXVolume = "SFXVolume";
    public const string QualityLevel = "QualityLevel";
    public const string ResolutionIndex = "ResolutionIndex";
    public const string Fullscreen = "Fullscreen";
    public const string VSync = "VSync";
    public const string MouseSensitivity = "MouseSensitivity";
    public const string InvertYAxis = "InvertYAxis";
    public const string ShowHints = "ShowHints";

    // Simulation
    public const string SelectedMode = "SelectedMode";
    public const string SelectedDifficulty = "SelectedDifficulty";
}
