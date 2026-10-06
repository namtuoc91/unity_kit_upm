using System;
using System.Collections.Generic;
using Raccoon.GameService;
using Raccoon.Purchase;
using UnityEngine;
#if USING_PURCHASE
using UnityEngine.Purchasing;
#endif
namespace Raccoon.Store
{
    public class GameStoreController : MonoBehaviour
    {
        private static GameStoreController api;
        public static GameStoreController Instance => api;

        /// <summary>Gọi khi đã fetch xong product từ store (giá local đã sẵn sàng).</summary>
        public static event Action OnProductsFetchedEvent;

        public List<IAPProductData> lstProduct;
#if USING_PURCHASE
        private StoreController m_StoreController;
#endif
        private bool _initialize = false;

        private System.Action<bool, string> onBuyComplete;
        private System.Action<bool, string> onRestoreComplete;

        void Awake()
        {
            api = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            //InitializePurchasing();
            Initialize();
        }

        public bool Initialized => _initialize;
        
#if USING_PURCHASE

        async void Initialize()
        {
            try
            {
                m_StoreController = UnityIAPServices.StoreController();

                m_StoreController.OnPurchasePending += OnPurchasePending;
                m_StoreController.OnPurchaseConfirmed += OnPurchaseConfirmed;
                m_StoreController.OnPurchaseFailed += OnPurchaseFailed;
                m_StoreController.OnCheckEntitlement += OnCheckEntitlement;
                m_StoreController.OnStoreDisconnected += OnStoreDisconnected;

                await m_StoreController.Connect();
                FetchProducts();
            }
            catch (Exception exception)
            {
                _initialize = true;
            }
        }
        
        void FetchProducts()
        {
            var products = new List<ProductDefinition>();
            foreach (var item in lstProduct)
            {
                products.Add(new ProductDefinition(item.id, item.productType));
            }
            m_StoreController.OnProductsFetched += OnProductsFetched;
            m_StoreController.OnProductsFetchFailed += OnProductsFetchFailed;
            m_StoreController.FetchProducts(products);
            _initialize = true;
        }

        #region IDetailedStoreListener Method
        
        void OnPurchasePending(PendingOrder order)
        {
            m_StoreController.ConfirmPurchase(order);
        }

        void OnPurchaseConfirmed(Order order)
        {
            // CheckSubscription();
            var cart = order.CartOrdered;
            foreach (var item in cart.Items())
            {
                string id = item.Product.definition.id;
                int qty = item.Quantity;

                Debug.Log($"UnityIAP: Purchase confirmed {qty} x {id}");

                // Call callback if needed
                onBuyComplete?.Invoke(true, id);
            }

            onBuyComplete = null;
        }

        void OnPurchaseFailed(FailedOrder order)
        {
            foreach (var item in order.CartOrdered.Items())
            {
                string id = item.Product.definition.id;
                Debug.Log($"UnityIAP: Purchase failed {id}. Reason: {order.FailureReason}");
                onBuyComplete?.Invoke(false, id);
            }

            onBuyComplete = null;
        }

        void OnStoreDisconnected(StoreConnectionFailureDescription storeConnectionFailureDescription)
        {
            Debug.Log($"Store disconnected. Reason: {storeConnectionFailureDescription}");
            // Optionally, update UI
        }
        
        void OnCheckEntitlement(Entitlement entitlement)
        {
            if (entitlement.Product != null)
            {
                string id = entitlement.Product.definition.id;
                switch (entitlement.Status)
                {
                    case EntitlementStatus.FullyEntitled:
                    case EntitlementStatus.EntitledButNotFinished:
                        Debug.Log($"UnityIAP: User owns product {id}");
                        // Unlock content here
                        onRestoreComplete?.Invoke(true, id);
                        break;

                    default:
                        Debug.Log($"UnityIAP: User does NOT own product {id}");
                        onRestoreComplete?.Invoke(false, id);
                        break;
                }
            }
        }
        
        void OnProductsFetchFailed(ProductFetchFailed productFetchFailed)
        {
            Debug.Log($"Product fetch failed. Reason: {productFetchFailed.FailureReason}");
            // Optionally, update UI or retry fetching products
        }

        void OnProductsFetched(List<Product> products)
        {
            Debug.Log("Products successfully fetched from the store.");
            OnProductsFetchedEvent?.Invoke();
        }
        #endregion

        #region Helper

        public static void BuyProductById(string id, System.Action<bool, string> onAction)
        {
            if (Instance == null)
            {
                onAction?.Invoke(false, id);
                return;
            }
            Instance.BuyProduct(id, onAction);
            GameFirebase.SendEvent(Event_Firebase.EVENT_PURCHASE_IAP, Event_Firebase.EVENT_PURCHASE_IAP, id);
        }

        public Product GetProductInfo(string id)
        {
            if (m_StoreController == null) return null;
            var product = m_StoreController.GetProductById(id);
            return product;
        }

        private void BuyProduct(string id, System.Action<bool, string> onAction)
        {
            if (m_StoreController == null)
            {
                onAction?.Invoke(false, id);
                return;
            }
            Debug.Log("BuyProductById  " + id);
#if UNITY_EDITOR
            onAction?.Invoke(true, id);
            return;
#endif
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                onAction?.Invoke(false, id);
                return;
            }

            // Fetch the currency Product reference from Unity Purchasing
            Product product = m_StoreController.GetProductById(id);
            if (product == null || !product.availableToPurchase)
            {
                onAction?.Invoke(false, id);
                return;
            }
            
            onBuyComplete = onAction;
            m_StoreController.PurchaseProduct(product);
        }

        public void CheckRestoreProductById(string id, Action<bool, string> onAction)
        {
            if (m_StoreController == null)
            {
                onAction?.Invoke(false, id);
                return;
            }
            Product product = m_StoreController.GetProductById(id);
            if (product == null)
            {
                onAction?.Invoke(false, id);
                return;
            }
            onRestoreComplete = onAction;
            m_StoreController.CheckEntitlement(product);
        }

        public bool IsSubscribedTo(Product subscription)
        {
            // If the product doesn't have a receipt, then it wasn't purchased and the user is therefore not subscribed.
            if (subscription == null || subscription.receipt == null)
            {
                return false;
            }

            try
            {
                //string intro_json = null;
                //The intro_json parameter is optional and is only used for the App Store to get introductory information.
                var subscriptionManager = new SubscriptionManager(subscription, null);

                // The SubscriptionInfo contains all of the information about the subscription.
                // Find out more: https://docs.unity3d.com/Packages/com.unity.purchasing@3.1/manual/UnityIAPSubscriptionProducts.html
                var info = subscriptionManager.getSubscriptionInfo();
                if (info == null) return false;
                return info.isSubscribed() == Result.True;
            }
            catch
            {
                //return false;
            }
            return false;
        }

        public bool IsSubscribedTo(string productID)
        {
            try
            {
                if (m_StoreController == null) return false;
                var subscription = m_StoreController.GetProductById(productID);
                // If the product doesn't have a receipt, then it wasn't purchased and the user is therefore not subscribed.
                if (subscription == null || subscription.receipt == null)
                {
                    return false;
                }
                //string intro_json = null;
                //The intro_json parameter is optional and is only used for the App Store to get introductory information.
                var subscriptionManager = new SubscriptionManager(subscription, null);

                // The SubscriptionInfo contains all of the information about the subscription.
                // Find out more: https://docs.unity3d.com/Packages/com.unity.purchasing@3.1/manual/UnityIAPSubscriptionProducts.html
                var info = subscriptionManager.getSubscriptionInfo();
                if (info == null) return false;
                return info.isSubscribed() == Result.True;
            }
            catch
            {
                //return false;
            }
            return false;
        }

        public TimeSpan GetExpireDate(string productID)
        {
            try
            {
                if (m_StoreController == null) return TimeSpan.Zero;
                var product = m_StoreController.GetProductById(productID);
                if (product != null && IsSubscribedTo(product))
                {
                    var subscriptionManager = new SubscriptionManager(product, null);
                    var info = subscriptionManager.getSubscriptionInfo();

                    if (info != null)
                    {
                        return info.getRemainingTime();
                    }
                }
                return TimeSpan.Zero;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        public static string GetPriceProductById(string id)
        {
            if (string.IsNullOrEmpty(id) || Instance == null) return string.Empty;

            var product = Instance.GetProductInfo(id);
            if(product != null && product.metadata != null && !string.IsNullOrEmpty(product.metadata.localizedPriceString))
            {
                return product.metadata.localizedPriceString;

            }
            return string.Empty;
        }
        #endregion
#else
        void Initialize()
        {
            _initialize = true;
        }
        
        public void CheckRestoreProductById(string id, Action<bool, string> onAction)
        {
            onAction?.Invoke(false, id);
            return;
        }
        
        public static string GetPriceProductById(string id)
        {
            return string.Empty;
        }
        
        public static void BuyProductById(string id, System.Action<bool, string> onAction)
        {
            onAction?.Invoke(Instance != null, id);
            return;
        }
#endif
    }
}