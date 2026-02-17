using UnityEngine;

//[Cilboxable]
public class RotateCube : MonoBehaviour
{
    [SerializeField] private Vector3 rotationSpeed = new Vector3(0, 100f, 0);

    private void Update()
    {
        transform.Rotate(rotationSpeed * Time.deltaTime);
    }
}
