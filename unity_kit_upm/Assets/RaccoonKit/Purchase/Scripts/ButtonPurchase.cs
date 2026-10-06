using System.Globalization;
using Raccoon.Purchase;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Raccoon.Store
{
    [RequireComponent(typeof(Button))]
    public class ButtonPurchase : MonoBehaviour
    {
        [SerializeField] private IAPProductData product;
        [SerializeField] private TMP_Text priceText;
        [Tooltip("Dùng khi text giá là UI Text thường thay vì TextMeshPro")]
        [SerializeField] private Text legacyPriceText;
        [Tooltip("Format cho text giá, {0} là giá. VD: \"Buy {0}\"")]
        [SerializeField] private string priceFormat = "{0}";

        public UnityEvent<string> onPurchaseSuccess;
        public UnityEvent<string> onPurchaseFailed;

        private Button button;
        private bool isPurchasing;

        public IAPProductData Product => product;

        void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(OnClickPurchase);
        }

        void OnEnable()
        {
            GameStoreController.OnProductsFetchedEvent += RefreshPrice;
            RefreshPrice();
        }

        void OnDisable()
        {
            GameStoreController.OnProductsFetchedEvent -= RefreshPrice;
        }

        public void SetProduct(IAPProductData data)
        {
            product = data;
            RefreshPrice();
        }

        public void RefreshPrice()
        {
            if (product == null || (priceText == null && legacyPriceText == null)) return;

            var price = GameStoreController.GetPriceProductById(product.id);
            if (string.IsNullOrEmpty(price))
                price = "$" + product.price_default.ToString("0.00", CultureInfo.InvariantCulture);

            var text = string.Format(priceFormat, price);
            if (priceText != null)
                priceText.text = text;
            if (legacyPriceText != null)
                legacyPriceText.text = text;
        }

        private void OnClickPurchase()
        {
            if (product == null || isPurchasing) return;

            isPurchasing = true;
            button.interactable = false;
            GameStoreController.BuyProductById(product.id, OnPurchaseResult);
        }

        private void OnPurchaseResult(bool success, string id)
        {
            isPurchasing = false;
            if (button != null)
                button.interactable = true;

            product.OnSendCheckButton(success);
            if (success)
                onPurchaseSuccess?.Invoke(id);
            else
                onPurchaseFailed?.Invoke(id);
        }
    }
}
