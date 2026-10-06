using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;

namespace Raccoon.Helpers
{
    public class LoadingDotsText : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI txtLoading;
        [SerializeField] private int maxDots = 3;
        [SerializeField] private float dotInterval = 0.4f;

        private string baseText;

        private void OnEnable()
        {
            if (txtLoading == null) return;

            baseText = txtLoading.text.TrimEnd('.');
            StartCoroutine(AnimateDots());
        }

        private void OnDisable()
        {
            StopAllCoroutines();

            if (txtLoading != null)
                txtLoading.text = baseText;
        }

        private IEnumerator AnimateDots()
        {
            var wait = new WaitForSecondsRealtime(dotInterval);
            var sb = new StringBuilder();
            int dotCount = 0;
            while (txtLoading != null)
            {
                sb.Clear();
                sb.Append(baseText);
                sb.Append('.', dotCount);
                txtLoading.text = sb.ToString();

                dotCount = (dotCount + 1) % (maxDots + 1);
                yield return wait;
            }
        }
    }
}
