using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;
using TMPro;

public class SettingsPopupController : MonoBehaviour
{
    [Header("═══ 팝업 패널 ═══")]
    [SerializeField] private GameObject popupPanel;
    [SerializeField] private Button closeButton;
    
    [Header("═══ 오디오 설정 ═══")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider bgmVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private TextMeshProUGUI masterVolumeText;
    [SerializeField] private TextMeshProUGUI bgmVolumeText;
    [SerializeField] private TextMeshProUGUI sfxVolumeText;
    [SerializeField] private AudioMixer audioMixer; // 옵션
    
    [Header("═══ 그래픽 설정 ═══")]
    [SerializeField] private Dropdown qualityDropdown;
    [SerializeField] private Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Toggle vsyncToggle;
    
    [Header("═══ 게임플레이 설정 ═══")]
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private TextMeshProUGUI mouseSensitivityText;
    [SerializeField] private Toggle invertYAxisToggle;
    [SerializeField] private Toggle showHintsToggle;
    
    [Header("═══ 버튼 ═══")]
    [SerializeField] private Button applyButton;
    [SerializeField] private Button defaultButton;
    
    private CanvasGroup canvasGroup;
    private Resolution[] resolutions;
    
    // 설정 값 저장
    private SettingsData currentSettings;
    private SettingsData tempSettings;
    
    [System.Serializable]
    public class SettingsData
    {
        public float masterVolume = 1f;
        public float bgmVolume = 0.8f;
        public float sfxVolume = 0.9f;
        public int qualityLevel = 2;
        public int resolutionIndex = 0;
        public bool fullscreen = true;
        public bool vsync = true;
        public float mouseSensitivity = 1f;
        public bool invertYAxis = false;
        public bool showHints = true;
    }
    
    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        
        LoadSettings();
        GetAvailableResolutions();
    }
    
    void Start()
    {
        SetupUI();
        ApplyCurrentSettings();
    }
    
    void GetAvailableResolutions()
    {
        resolutions = Screen.resolutions;
        
        if (resolutionDropdown != null)
        {
            resolutionDropdown.ClearOptions();
            List<string> options = new List<string>();
            
            int currentResolutionIndex = 0;
            for (int i = 0; i < resolutions.Length; i++)
            {
                string option = resolutions[i].width + " x " + resolutions[i].height + " @ " + resolutions[i].refreshRate + "Hz";
                options.Add(option);
                
                if (resolutions[i].width == Screen.currentResolution.width &&
                    resolutions[i].height == Screen.currentResolution.height)
                {
                    currentResolutionIndex = i;
                }
            }
            
            resolutionDropdown.AddOptions(options);
            resolutionDropdown.value = currentResolutionIndex;
            resolutionDropdown.RefreshShownValue();
        }
    }
    
    void SetupUI()
    {
        // 닫기 버튼
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePopup);
        }
        
        // 오디오 슬라이더
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            masterVolumeSlider.value = currentSettings.masterVolume;
        }
        
        if (bgmVolumeSlider != null)
        {
            bgmVolumeSlider.onValueChanged.AddListener(OnBGMVolumeChanged);
            bgmVolumeSlider.value = currentSettings.bgmVolume;
        }
        
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
            sfxVolumeSlider.value = currentSettings.sfxVolume;
        }
        
        // 그래픽 설정
        if (qualityDropdown != null)
        {
            qualityDropdown.ClearOptions();
            qualityDropdown.AddOptions(new List<string> { "낮음", "중간", "높음", "매우 높음" });
            qualityDropdown.value = currentSettings.qualityLevel;
            qualityDropdown.onValueChanged.AddListener(OnQualityChanged);
        }
        
        if (resolutionDropdown != null)
        {
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        }
        
        if (fullscreenToggle != null)
        {
            fullscreenToggle.isOn = currentSettings.fullscreen;
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        }
        
        if (vsyncToggle != null)
        {
            vsyncToggle.isOn = currentSettings.vsync;
            vsyncToggle.onValueChanged.AddListener(OnVSyncChanged);
        }
        
        // 게임플레이 설정
        if (mouseSensitivitySlider != null)
        {
            mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSensitivityChanged);
            mouseSensitivitySlider.value = currentSettings.mouseSensitivity;
        }
        
        if (invertYAxisToggle != null)
        {
            invertYAxisToggle.isOn = currentSettings.invertYAxis;
            invertYAxisToggle.onValueChanged.AddListener(OnInvertYAxisChanged);
        }
        
        if (showHintsToggle != null)
        {
            showHintsToggle.isOn = currentSettings.showHints;
            showHintsToggle.onValueChanged.AddListener(OnShowHintsChanged);
        }
        
        // 버튼
        if (applyButton != null)
        {
            applyButton.onClick.AddListener(ApplySettings);
        }
        
        if (defaultButton != null)
        {
            defaultButton.onClick.AddListener(ResetToDefault);
        }
    }
    
    // ═══════════════ 설정 변경 핸들러 ═══════════════
    
    void OnMasterVolumeChanged(float value)
    {
        tempSettings.masterVolume = value;
        if (masterVolumeText != null)
            masterVolumeText.text = Mathf.RoundToInt(value * 100) + "%";
    }
    
    void OnBGMVolumeChanged(float value)
    {
        tempSettings.bgmVolume = value;
        if (bgmVolumeText != null)
            bgmVolumeText.text = Mathf.RoundToInt(value * 100) + "%";
    }
    
    void OnSFXVolumeChanged(float value)
    {
        tempSettings.sfxVolume = value;
        if (sfxVolumeText != null)
            sfxVolumeText.text = Mathf.RoundToInt(value * 100) + "%";
    }
    
    void OnQualityChanged(int index)
    {
        tempSettings.qualityLevel = index;
    }
    
    void OnResolutionChanged(int index)
    {
        tempSettings.resolutionIndex = index;
    }
    
    void OnFullscreenChanged(bool value)
    {
        tempSettings.fullscreen = value;
    }
    
    void OnVSyncChanged(bool value)
    {
        tempSettings.vsync = value;
    }
    
    void OnMouseSensitivityChanged(float value)
    {
        tempSettings.mouseSensitivity = value;
        if (mouseSensitivityText != null)
            mouseSensitivityText.text = value.ToString("F1");
    }
    
    void OnInvertYAxisChanged(bool value)
    {
        tempSettings.invertYAxis = value;
    }
    
    void OnShowHintsChanged(bool value)
    {
        tempSettings.showHints = value;
    }
    
    // ═══════════════ 설정 적용 ═══════════════
    
    void ApplySettings()
    {
        currentSettings = tempSettings;
        ApplyCurrentSettings();
        SaveSettings();
        
        Debug.Log("설정이 적용되었습니다.");
    }
    
    void ApplyCurrentSettings()
    {
        // 오디오 설정 적용
        AudioListener.volume = currentSettings.masterVolume;
        
        if (audioMixer != null)
        {
            audioMixer.SetFloat("MasterVolume", Mathf.Log10(currentSettings.masterVolume) * 20);
            audioMixer.SetFloat("BGMVolume", Mathf.Log10(currentSettings.bgmVolume) * 20);
            audioMixer.SetFloat("SFXVolume", Mathf.Log10(currentSettings.sfxVolume) * 20);
        }
        
        // 그래픽 설정 적용
        QualitySettings.SetQualityLevel(currentSettings.qualityLevel);
        
        if (resolutions != null && currentSettings.resolutionIndex < resolutions.Length)
        {
            Resolution resolution = resolutions[currentSettings.resolutionIndex];
            Screen.SetResolution(resolution.width, resolution.height, currentSettings.fullscreen);
        }
        
        QualitySettings.vSyncCount = currentSettings.vsync ? 1 : 0;
        
        // 게임플레이 설정은 다른 시스템에서 참조
        tempSettings = currentSettings;
    }
    
    void ResetToDefault()
    {
        currentSettings = new SettingsData();
        tempSettings = currentSettings;
        
        // UI 업데이트
        UpdateUIWithSettings();
        ApplyCurrentSettings();
        SaveSettings();
        
        Debug.Log("설정이 기본값으로 초기화되었습니다.");
    }
    
    void UpdateUIWithSettings()
    {
        if (masterVolumeSlider != null) masterVolumeSlider.value = currentSettings.masterVolume;
        if (bgmVolumeSlider != null) bgmVolumeSlider.value = currentSettings.bgmVolume;
        if (sfxVolumeSlider != null) sfxVolumeSlider.value = currentSettings.sfxVolume;
        if (qualityDropdown != null) qualityDropdown.value = currentSettings.qualityLevel;
        if (fullscreenToggle != null) fullscreenToggle.isOn = currentSettings.fullscreen;
        if (vsyncToggle != null) vsyncToggle.isOn = currentSettings.vsync;
        if (mouseSensitivitySlider != null) mouseSensitivitySlider.value = currentSettings.mouseSensitivity;
        if (invertYAxisToggle != null) invertYAxisToggle.isOn = currentSettings.invertYAxis;
        if (showHintsToggle != null) showHintsToggle.isOn = currentSettings.showHints;
    }
    
    // ═══════════════ 저장/불러오기 ═══════════════
    
    void SaveSettings()
    {
        PlayerPrefs.SetFloat("MasterVolume", currentSettings.masterVolume);
        PlayerPrefs.SetFloat("BGMVolume", currentSettings.bgmVolume);
        PlayerPrefs.SetFloat("SFXVolume", currentSettings.sfxVolume);
        PlayerPrefs.SetInt("QualityLevel", currentSettings.qualityLevel);
        PlayerPrefs.SetInt("ResolutionIndex", currentSettings.resolutionIndex);
        PlayerPrefs.SetInt("Fullscreen", currentSettings.fullscreen ? 1 : 0);
        PlayerPrefs.SetInt("VSync", currentSettings.vsync ? 1 : 0);
        PlayerPrefs.SetFloat("MouseSensitivity", currentSettings.mouseSensitivity);
        PlayerPrefs.SetInt("InvertYAxis", currentSettings.invertYAxis ? 1 : 0);
        PlayerPrefs.SetInt("ShowHints", currentSettings.showHints ? 1 : 0);
        PlayerPrefs.Save();
    }
    
    void LoadSettings()
    {
        currentSettings = new SettingsData();
        
        currentSettings.masterVolume = PlayerPrefs.GetFloat("MasterVolume", 1f);
        currentSettings.bgmVolume = PlayerPrefs.GetFloat("BGMVolume", 0.8f);
        currentSettings.sfxVolume = PlayerPrefs.GetFloat("SFXVolume", 0.9f);
        currentSettings.qualityLevel = PlayerPrefs.GetInt("QualityLevel", 2);
        currentSettings.resolutionIndex = PlayerPrefs.GetInt("ResolutionIndex", 0);
        currentSettings.fullscreen = PlayerPrefs.GetInt("Fullscreen", 1) == 1;
        currentSettings.vsync = PlayerPrefs.GetInt("VSync", 1) == 1;
        currentSettings.mouseSensitivity = PlayerPrefs.GetFloat("MouseSensitivity", 1f);
        currentSettings.invertYAxis = PlayerPrefs.GetInt("InvertYAxis", 0) == 1;
        currentSettings.showHints = PlayerPrefs.GetInt("ShowHints", 1) == 1;
        
        tempSettings = currentSettings;
    }
    
    // ═══════════════ 팝업 제어 ═══════════════
    
    public void ShowPopup()
    {
        gameObject.SetActive(true);
        StartCoroutine(AnimateOpen());
    }
    
    public void ClosePopup()
    {
        StartCoroutine(AnimateClose());
    }
    
    IEnumerator AnimateOpen()
    {
        if (popupPanel != null)
        {
            popupPanel.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            canvasGroup.alpha = 0f;
            
            float duration = 0.2f;
            float elapsed = 0f;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / duration;
                
                popupPanel.transform.localScale = Vector3.Lerp(new Vector3(0.8f, 0.8f, 1f), Vector3.one, progress);
                canvasGroup.alpha = progress;
                
                yield return null;
            }
            
            popupPanel.transform.localScale = Vector3.one;
            canvasGroup.alpha = 1f;
        }
    }
    
    IEnumerator AnimateClose()
    {
        float duration = 0.15f;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = 1f - (elapsed / duration);
            
            if (popupPanel != null)
            {
                popupPanel.transform.localScale = Vector3.Lerp(new Vector3(0.9f, 0.9f, 1f), Vector3.one, progress);
            }
            
            canvasGroup.alpha = progress;
            
            yield return null;
        }
        
        gameObject.SetActive(false);
    }
    
    // 외부에서 설정값 접근
    public static SettingsData GetCurrentSettings()
    {
        SettingsPopupController controller = FindObjectOfType<SettingsPopupController>();
        if (controller != null)
        {
            return controller.currentSettings;
        }
        return new SettingsData();
    }
    
    void OnDestroy()
    {
        // 리스너 정리
        if (closeButton != null) closeButton.onClick.RemoveAllListeners();
        if (applyButton != null) applyButton.onClick.RemoveAllListeners();
        if (defaultButton != null) defaultButton.onClick.RemoveAllListeners();
    }
}
