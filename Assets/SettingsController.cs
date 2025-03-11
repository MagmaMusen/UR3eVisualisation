using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class SettingsController : MonoBehaviour
{
    // Start is called before the first frame update
    public List<TMP_InputField> InputFields;

    void Start()
    {
        foreach (var inputField in InputFields)
        {
            
            string parentName = inputField.transform.parent.name;
            string value = PlayerPrefs.GetString(parentName, DefaultSettings.Lookup[parentName]);
            inputField.text = value;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
