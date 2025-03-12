using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Controls the visibility of specified GameObjects by toggling their active state
/// when the 'T' key is pressed.
/// </summary>
public class HideObjectsController : MonoBehaviour
{
    private GameObject hitPointMarker;
    private GameObject menuButton;

    void Start()
    {
        menuButton = GameObject.Find("MenuButton");
        hitPointMarker = GameObject.Find("HitPointMarker(Clone)");

        if (hitPointMarker == null)
            Debug.LogWarning("HitPointMarker not found in the scene!");

        if (menuButton == null)
            Debug.LogWarning("MenuButton not found in the scene!");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (hitPointMarker == null) hitPointMarker = GameObject.Find("HitPointMarker(Clone)");
            ToggleObject(hitPointMarker);
            ToggleObject(menuButton);
        }
    }

    void ToggleObject(GameObject obj)
    {
        if (obj != null)
        {
            Debug.Log($"Toggle {obj.name}");
            obj.SetActive(!obj.activeSelf);
        }
    }
}

