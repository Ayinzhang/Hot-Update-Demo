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

        // 屏幕宽高（Canvas尺寸下）
        var canvas = GetComponentInParent<Canvas>();
        var canvasRect = canvas.GetComponent<RectTransform>().rect;

        // RawImage物体自身的宽高
        imageWidth = rectTrans.rect.width;
        imageHeight = rectTrans.rect.height;

        // min/max保证不会超出
        minPos = new Vector2(-canvasRect.width / 2 + imageWidth / 2, -canvasRect.height / 2 + imageHeight / 2);
        maxPos = new Vector2(canvasRect.width / 2 - imageWidth / 2, canvasRect.height / 2 - imageHeight / 2);

        StartCoroutine(MoveRoutine());
    }

    IEnumerator MoveRoutine()
    {
        while (true)
        {
            // 随机一个新目标位置与旋转
            Vector2 targetPos = new Vector2(
                Random.Range(minPos.x, maxPos.x),
                Random.Range(minPos.y, maxPos.y)
            );
            float targetRot = Random.Range(0f, 360f);

            // 缓动时间
            float moveTime = Random.Range(1f, 2.5f);
            float elapsed = 0f;

            Vector2 startPos = rectTrans.anchoredPosition;
            float startRot = rectTrans.localEulerAngles.z;

            // 平滑移动和旋转
            while (elapsed < moveTime)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / moveTime);

                rectTrans.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);

                // 角度插值（z轴旋转，使用 Mathf.LerpAngle 获得跨0~360度平滑旋转）
                rectTrans.localEulerAngles = new Vector3(0, 0, Mathf.LerpAngle(startRot, targetRot, t));

                yield return null;
            }

            // 停一会
            yield return new WaitForSeconds(Random.Range(0.5f, 1.5f));
        }
    }
}