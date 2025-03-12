using UnityEngine;
using UnityEngine.SceneManagement;

public class Menu : MonoBehaviour
{
    public static bool MenuIsActive = false;
    public GameObject MenuUI;
    public GameObject MenuButton;

    // Update is called once per frame
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Escape))
        {
            if (MenuIsActive)
            {
                Resume();
            } else
            {
                ActivateMenu();
            }
        }
    }

    public void Resume ()
    {
        MenuUI.SetActive(false);
        MenuButton.SetActive(true);
        MenuIsActive = false;
    }

    public void ActivateMenu()
    {
        MenuUI.SetActive(true);
        MenuButton.SetActive(false);
        MenuIsActive = true;
    }

    public void Quit ()
    {
        Debug.Log("Quitting game...");
        Application.Quit();
    }

    public void Settings()
    {
        SceneManager.LoadScene(1);
    }
}
