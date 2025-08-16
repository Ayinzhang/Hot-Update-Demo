using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class CatMove : MonoBehaviour
{
    RectTransform rectTrans;
    Vector2 minPos, maxPos;
    float imageWidth, imageHeight;

    void Start()
    {
        rectTrans = GetComponent<RectTransform>();

        var canvas = GetComponentInParent<Canvas>();
        var canvasRect = canvas.GetComponent<RectTransform>().rect;

        imageWidth = rectTrans.rect.width;
        imageHeight = rectTrans.rect.height;

        minPos = new Vector2(-canvasRect.width / 2 + imageWidth / 2, -canvasRect.height / 2 + imageHeight / 2);
        maxPos = new Vector2(canvasRect.width / 2 - imageWidth / 2, canvasRect.height / 2 - imageHeight / 2);

        StartCoroutine(MoveRoutine());
    }

    IEnumerator MoveRoutine()
    {
        while (true)
        {
            Vector2 targetPos = new Vector2(
                Random.Range(minPos.x, maxPos.x),
                Random.Range(minPos.y, maxPos.y)
            );
            float targetRot = Random.Range(0f, 360f);

            float moveTime = Random.Range(1f, 2.5f);
            float elapsed = 0f;

            Vector2 startPos = rectTrans.anchoredPosition;
            float startRot = rectTrans.localEulerAngles.z;

            while (elapsed < moveTime)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / moveTime);

                rectTrans.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
                rectTrans.localEulerAngles = new Vector3(0, 0, Mathf.LerpAngle(startRot, targetRot, t));

                yield return null;
            }

            yield return new WaitForSeconds(Random.Range(0.5f, 1.5f));
        }
    }
}