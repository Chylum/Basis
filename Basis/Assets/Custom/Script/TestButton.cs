using TMPro;
using UnityEngine;

public class TestButton : MonoBehaviour
{
    [SerializeField] private TextMeshPro playerChatText;

    public void OnButtonClick()
    {
        BasisDebug.Log("TestButton: Button clicked.");
        string message = "Give me food";

        playerChatText.text = message;
    }
}
