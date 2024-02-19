using Managers;
using Models;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.UI;

public class InputFieldBase : MonoBehaviour, IRefreshable
{
    
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TextMeshProUGUI inputFieldText;
    [SerializeField] private TextMeshProUGUI placeholderText;
    [SerializeField] private Image inputFieldOutline;
    public Image symbolImage;
    private Image _inputFieldImage;

    // Start is called before the first frame update
    private void Start()
    {
        _inputFieldImage = inputField.GetComponent<Image>();
        Refresh();
    }
    
    public void Refresh()
    {
        if(_inputFieldImage == null)
            _inputFieldImage = inputField.GetComponent<Image>();
        UIManager ui = UIManager.Instance;
        Color newTextColor = ui.GetColor(ColorType.BaseForeground);
        Color newOutlineColor = ui.GetColor(ColorType.Transparent);
        Color newBackgroundColor = ui.GetColor(ColorType.HighlightedBackground);
        Color newSymbolColor = ui.GetColor(ColorType.BaseForeground);
        Color newPlaceholderColor = ui.GetColor(ColorType.PlaceholderForeground);
        
        if(_inputFieldImage.color != newBackgroundColor)
            UIManager.Instance.Animator.FadeColor(_inputFieldImage, _inputFieldImage.color, newBackgroundColor, ui.FadeBaseDuration);
        if(placeholderText.color != newPlaceholderColor)
            UIManager.Instance.Animator.FadeTextColor(placeholderText, placeholderText.color, newPlaceholderColor, ui.FadeBaseDuration);
        if(inputFieldText.color != newTextColor)
            UIManager.Instance.Animator.FadeTextColor(inputFieldText, inputFieldText.color, newTextColor, ui.FadeBaseDuration);
        if(inputFieldOutline.color != newOutlineColor)
            UIManager.Instance.Animator.FadeColor(inputFieldOutline, inputFieldOutline.color, newOutlineColor, ui.FadeBaseDuration);
        if(symbolImage.color != newSymbolColor)
            UIManager.Instance.Animator.FadeColor(symbolImage, symbolImage.color, newSymbolColor, ui.FadeBaseDuration);
    }

    public void OnInputFieldGotFocus()
    {
        UIManager ui = UIManager.Instance;
        LeanTween.value(inputFieldOutline.gameObject, ThemeManager.GetColor(ColorType.Transparent, Theme.Dark), 
                ui.PrimaryBackgroundColor, ui.FadeBaseDuration)
            .setOnUpdateColor(color => inputFieldOutline.color = color);
        LeanTween.value(symbolImage.gameObject, ThemeManager.GetColor(ColorType.BaseForeground, ui.currentTheme), 
                ui.PrimaryBackgroundColor, ui.FadeBaseDuration)
            .setOnUpdateColor(color => symbolImage.color = color);
        if (!string.IsNullOrEmpty(inputField.text))
        {
            LeanTween.value(inputField.gameObject, ThemeManager.GetColor(ColorType.BaseForeground, ui.currentTheme), 
                    ThemeManager.GetColor(ColorType.HighlightedForeground, ui.currentTheme), ui.FadeBaseDuration)
                .setOnUpdateColor(color => inputFieldText.color = color);
        }
    }
    
    public void OnInputFieldLostFocus()
    {
        UIManager ui = UIManager.Instance;
        LeanTween.value(inputFieldOutline.gameObject, ui.PrimaryBackgroundColor, ThemeManager.GetColor(ColorType.Transparent, Theme.Dark), 
                ui.FadeBaseDuration)
            .setOnUpdateColor(color => inputFieldOutline.color = color);
        LeanTween.value(symbolImage.gameObject, ui.PrimaryBackgroundColor, 
                ThemeManager.GetColor(ColorType.BaseForeground, ui.currentTheme), ui.FadeBaseDuration)
            .setOnUpdateColor(color => symbolImage.color = color);
        if (!string.IsNullOrEmpty(inputField.text))
        {
            LeanTween.value(inputField.gameObject, ThemeManager.GetColor(ColorType.HighlightedForeground, ui.currentTheme), 
                    ThemeManager.GetColor(ColorType.BaseForeground, ui.currentTheme), ui.FadeBaseDuration)
                .setOnUpdateColor(color => inputFieldText.color = color);
        }
    }
    
}
