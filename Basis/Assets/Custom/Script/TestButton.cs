using TMPro;
using UnityEngine;

public class TestButton : MonoBehaviour
{
    public UnityEngine.UI.Button buttonControl1;

    [SerializeField] private TextMeshPro buttonText;

    private void Awake()
    {
        buttonControl1.onClick.AddListener(OnButtonClick);
    }

    public void OnButtonClick()
    {
        buttonText.text = "Clicked!";
    }
}
