using UnityEngine;

public class MultiDisplay : MonoBehaviour
{
    [Header("Multi Display")]
    public bool activateOnStart = true;

    void Start()
    {
        if (activateOnStart)
        {
            ActivateDisplays();
        }
    }

    [ContextMenu("Activate Displays")]
    public void ActivateDisplays()
    {
        // Display.displays[0] はメインディスプレイで常に有効
        for (int i = 1; i < Display.displays.Length; i++)
        {
            Display.displays[i].Activate();
            Debug.Log($"Activated Display {i + 1}");
        }
    }
}
