using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SettingsController : MonoBehaviour
{
    // Start is called before the first frame update
    public List<TMP_InputField> InputFields;

    void Start()
    {
        UpdateUI();
    }

    public void UpdateUI ()
    {
        foreach (var inputField in InputFields)
        {

            string key = getInputFieldKey(inputField);
            string value = PlayerPrefs.GetString(key, DefaultSettings.Lookup[key]);
            inputField.text = value;
        }
    }

    /// <summary>
    /// Saves the settings from the input fields to player prefs
    /// </summary>
    public void SaveSettings ()
    {
        foreach (var inputField in InputFields)
        {
            PlayerPrefs.SetString(getInputFieldKey(inputField), inputField.text);
        }
        PlayerPrefs.Save();
    }

    public void Resume ()
    {
        SceneManager.LoadScene(0);
    }

    public void SetToDefault ()
    {
        foreach (var inputField in InputFields)
        {
            var key = getInputFieldKey(inputField);
            PlayerPrefs.SetString(key, DefaultSettings.Lookup[key]);
        }
        PlayerPrefs.Save();
        UpdateUI();
    }


    private string getInputFieldKey (TMP_InputField inputField)
    {
        return inputField.transform.parent.name;
    }

}
