using JamKit;
using UnityEngine;

public class MenuController : MonoBehaviour
{
    public void OnPlayButtonClicked()
    {
        SceneLoader.LoadScene(GameConstants.SceneGame);
    }
}
