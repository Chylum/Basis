using Basis.Scripts.BasisSdk.Players;
using UnityEngine;
using static SerializableBasis;

public class PropSpawner : MonoBehaviour
{
    [SerializeField] private BasisRuntimeLoader runtimeLoader;

    public void OnButtonClick()
    {
        Debug.Log("PropSpawner: Button clicked. Attempting to load prop...");
        runtimeLoader.LoadNow();
        Debug.Log("PropSpawner: LoadNow called on BasisRuntimeLoader.");
    }
}
