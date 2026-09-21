using JamKit;
using UnityEngine;

public class BootController : MonoBehaviour
{
    // Read from Start, not Awake: Bootstrap's own Awake (either scene-placed or the
    // RuntimeInitializeOnLoadMethod fallback) is guaranteed to have run by Start, not before.
    private void Start()
    {
        SceneLoader.LoadScene(GameConstants.SceneMenu);
    }
}
