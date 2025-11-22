using UnityEngine;
using System.Collections;

public class SpriteFlash : MonoBehaviour
{
    private Material defaultMat;
    private Material flashMat;

    SpriteRenderer sr;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        defaultMat = sr.material;
        flashMat = Resources.Load<Material>("Materials/FlashMaterial");
    }

    public void Flash(float flashDuration)
    {
        StopAllCoroutines();
        StartCoroutine(DoFlash(flashDuration));
    }

    IEnumerator DoFlash(float flashDuration)
    {
        sr.material = flashMat;
        yield return new WaitForSecondsRealtime(flashDuration);
        sr.material = defaultMat;
    }
}
