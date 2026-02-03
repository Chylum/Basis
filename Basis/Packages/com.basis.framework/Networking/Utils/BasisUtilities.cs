using Basis.Scripts.Networking.NetworkedAvatar;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
public static class BasisUtilities
{
    public static bool IsValid(BasisNetworkPlayer Player)
    {
        if(Player == null)
        {
            return false;
        }

        return true;
    }

    public static bool IsValid(GameObject gameObject)
    {
        if(gameObject == null)
        {
            return false;
        }

        return true;
    }

    public static bool IsValid (Material material)
    {
        if(material == null)
        {
            return false;
        }
        return true;
    }

    public static bool IsValid(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }
        return true;
    }

    public static bool IsValid(Transform transform)
    {
        if (transform == null)
        {
            return false;
        }
        return true;
    }

    public static bool IsValid(LineRenderer lineRenderer)
    {
        if (lineRenderer == null)
        {
            return false;
        }
        return true;
    }

    public static bool IsValid(Text text)
    {
        if (text == null)
        {
            return false;
        }
        return true;
    }

    public static bool IsValid(TextMeshPro tmp)
    {
        if (tmp == null)
        {
            return false;
        }
        return true;
    }

    public static bool IsValid(TextMeshProUGUI tmpu)
    {
        if (tmpu == null)
        {
            return false;
        }
        return true;
    }

    public static bool IsValid(Image image)
    {
        if (image == null)
        {
            return false;
        }
        return true;
    }

}
