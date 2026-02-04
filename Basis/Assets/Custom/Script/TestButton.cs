using TMPro;
using UnityEngine;

[Cilboxable]
public class TestButton : MonoBehaviour
{
    [SerializeField] private TextMeshPro buttonText;

    public void OnButtonClick()
    {
        buttonText.text = "Clicked!";

    }
}
