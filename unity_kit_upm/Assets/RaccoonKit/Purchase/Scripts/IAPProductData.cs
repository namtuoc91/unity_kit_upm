using UnityEngine;
#if USING_PURCHASE
using UnityEngine.Purchasing;
#endif

namespace Raccoon.Purchase
{
    [CreateAssetMenu(fileName = "IAP_product_", menuName = "Raccoon/Purchase/ProductIAP")]
    public class IAPProductData : ScriptableObject
    {
        public string id;
#if USING_PURCHASE
        public ProductType productType;
#endif
        public float price_default;
        public Subscribe_reward_time subscribe_reward_time;
        public bool has_noads;
        public bool unlock_item;

        public System.Action<bool> OnClick;
        public System.Action<bool> OnCheckButton;

        public void OnSendCheckButton(bool success)
        {
            if (OnCheckButton != null)
                OnCheckButton?.Invoke(success);
        }
    }

    public enum Subscribe_reward_time
    {
        None = 0,
        Per_day,
        Per_Week,
        Per_Month,
        Per_Year,
    }
}