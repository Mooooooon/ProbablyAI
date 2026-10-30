using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using ProbablyAI.Hands;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Customers
{
    // Read live native data, without creating visitors, advancing dialogue or accepting deals.
    public sealed class GameCustomerBackend : ICustomerBackend
    {
        private readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
        private readonly string session = Guid.NewGuid().ToString("N").Substring(0, 8);
        private readonly Func<bool> available;
        private readonly GameHandsBackend hands;
        private StoreClientInstance observed;
        private string visitId;
        private int sequence;
        private bool active = true;

        public GameCustomerBackend(Func<bool> available, GameHandsBackend hands)
        { this.available = available; this.hands = hands; }

        public void Invalidate() => active = false;

        private void EnsureReady()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new HandsException("wrong_thread", T("客人信息必须在游戏主线程读取。"));
            if (!active || !available() || PlayerStore.Instance == null)
                throw new HandsException("store_unavailable", T("当前无法读取客人，请返回当铺并等待载入完成。"));
        }

        public CustomerSnapshot Capture()
        {
            EnsureReady();
            var instance = PlayerStore.Instance.currentClientInstance;
            if (!ReferenceEquals(observed, instance))
            {
                observed = instance;
                visitId = instance == null ? null : "guest-" + session + "-" + (++sequence);
            }
            if (instance == null) return new CustomerSnapshot { Status = "absent" };
            if (!PlayerStore.Instance.isClientArrived) return new CustomerSnapshot { Status = "arriving" };
            var client = instance.GetClientBlueprint(); // A getter, not an instantiation or reroll.
            if (client == null) return new CustomerSnapshot { Status = "arriving" };
            return new CustomerSnapshot
            {
                Status = "present",
                Customer = new CustomerInfo
                {
                    Id = visitId, Name = Plain(client.displayName), Identifier = client.identifier,
                    Faction = client.clientFaction, Intent = client.clientIntent.ToString(),
                    IntentDescription = DescribeIntent(client.clientIntent), BusinessComplete = client.isBusinessComplete,
                    PurchaseBudget = Budget(client),
                    Preferences = new CustomerPreferences
                    {
                        BuyingTags = Copy(client.clientBuyingTagList), BuyingIdentifiers = Copy(client.clientBuyingIdList),
                        RejectedTags = Copy(client.clientBlackTagList), RejectedIdentifiers = Copy(client.clientBlackIdList),
                        HasCustomPurchaseRule = client.willAcceptItem != null,
                        RequiresVariety = client.isRequireVariety, PreviouslyBoughtIdentifiers = Copy(client.varietyTrackingList),
                        MultiplePurchaseDisabled = client.isMultiBuyDisabled, RefusesContraband = client.noAcceptingContraband,
                        RefusesHazardousWaterLabel = client.refusesHazardousWaterLabel,
                        AcceptedChemicalProductGrade = client.acceptedChemicalProductGrade,
                        CustomerSellingDisabled = client.isRegularSellDisable,
                        Note = T("原始游戏标签/标识符。ALL_TYPE 表示通用购买，ALL_TYPE_EXCEPT_CONTRABAND 排除违禁品；状态、容器内容和自定义规则仍可能拒收。用 check_customer_item 核对具体物品；普通购买偏好不代表易货或委托条件。CustomerSellingDisabled 表示客人不向店主普通出售。")
                    },
                    CurrentDialogue = CurrentDialogue(client), Barter = Barter(client.barterOffer),
                    Procurement = Procurement(client), GunOrder = GunOrder(client.gunOrderData), ServiceCost = client.serviceCost
                }
            };
        }

        public CustomerItemCheck CheckItem(string expectedCustomerId, string itemId)
        {
            var snapshot = Capture();
            if (snapshot.Customer == null)
                throw new HandsException("no_customer", snapshot.Status == "arriving" ? T("客人尚未到达柜台。") : T("当前没有客人。"));
            if (snapshot.Customer.Id != expectedCustomerId)
                throw new HandsException("customer_changed", T("当前客人已改变，请重新查询客人及需求后再检查物品。"));
            var client = observed.GetClientBlueprint();
            var world = hands.Capture(); // Refresh the same ID map used by inventory tools, including nested containers.
            var info = world.Items.FirstOrDefault(i => i.Id == itemId);
            if (info == null) throw new HandsException("item_not_found", T("物品已失效或不可访问，请重新查询物品。"));
            var item = hands.ResolveCurrentItem(itemId);
            var pricing = GameCustomerPricing.Read(item, client);
            var budget = Budget(client);
            var result = new CustomerItemCheck
            {
                CustomerId = snapshot.Customer.Id, ItemId = info.Id, ItemName = info.Name, ContainerId = info.ContainerId,
                BusinessComplete = client.isBusinessComplete,
                MatchesRegularPurchaseRules = client.IsClientBuyingThisItem(item),
                PurchaseBudget = budget, CurrentItemValue = pricing.Value, Pricing = pricing,
                ValueIsEstimate = pricing.Source != "counter_offer",
                WithinRemainingBudget = CustomerPriceCalculator.WithinBudget(pricing, budget, GeneralHelper.IsItemOwned(item) && client.IsClientBuying()),
                Note = PriceNote(pricing.Source)
            };
            var conditions = client.barterOffer?.conditions;
            if (conditions != null)
                for (int index = 0; index < conditions.Count; index++)
                    if (conditions[index] != null && conditions[index].IsItemValid(item))
                        result.MatchingBarterRequirementIndexes.Add(index);
            return result;
        }

        private static string PriceNote(string source)
            => source == "counter_offer" ? T("当前金额已包含所有价格效果，可直接用于本次预算比较。") :
               source == "projected_counter_offer" ? T("上柜台前的参考报价，已计入已知价格效果；放上柜台后重新核对。") :
               T("当前物品价值；客人购买预算不用于主人买入物品。");

        private static CustomerBudget Budget(StoreClient client) => new CustomerBudget
        {
            Enabled = client.useClientBudget,
            Remaining = client.useClientBudget ? (int?)client.GetBudget() : null,
            Note = client.useClientBudget ? T("客人购买店主物品的实时剩余预算；不是初始预算、店主现金或委托报酬。") :
                T("此客人未启用普通购买预算；没有可报告的剩余数值，不能解释为零或无限现金。")
        };

        private static DialogueInfo CurrentDialogue(StoreClient client)
        {
            // Only the displayed line, never future branches, choice actions or hidden dialogue.
            var dialogue = client.isMainDialogueStarted ? DialogUIManager.Instance?.currentDialog : null;
            if (dialogue == null) return null;
            var text = dialogue.text?.Build(4000).nakedText ?? dialogue.plainText;
            return string.IsNullOrWhiteSpace(text) ? null : new DialogueInfo { Speaker = Plain(dialogue.title), Text = Plain(text) };
        }

        private static BarterNeeds Barter(BarterOffer offer)
        {
            if (offer == null) return null;
            var result = new BarterNeeds { Description = Plain(offer.barterTextBody), IsFreeReward = offer.isFreeReward };
            if (offer.conditions != null)
                for (int index = 0; index < offer.conditions.Count; index++)
                {
                    var condition = offer.conditions[index];
                    if (condition == null) continue;
                    result.Requirements.Add(new BarterRequirement
                    {
                        Index = index, RequiredIdentifier = condition.requiredItemId, RequiredTag = condition.requiredItemTag,
                        RequiredDisplay = Plain(condition.requiredDisplay), OfferedDisplay = Plain(condition.offeredDisplay),
                        RejectedConditions = Copy(condition.blacklistedConditionId)
                    });
                }
            return result;
        }

        private static ProcurementNeeds Procurement(StoreClient client)
        {
            var data = client.procurementData;
            if (data == null) return null;
            return new ProcurementNeeds
            {
                Description = Plain(client.clientIntent == StoreClient.ClientIntent.PROCUREMENT_COLLECT ? data.descriptionCollect : data.descriptionOffer),
                InitialReward = data.initialReward, FinalReward = data.finalReward
            };
        }

        private static GunOrderNeeds GunOrder(GunOrderData order)
        {
            if (order == null) return null;
            return new GunOrderNeeds
            {
                Description = Plain(order.dialogText), IsServiceOrder = order.isServiceOrder, BaseReward = order.baseReward,
                Mandatory = GunRequirements(order.mandatoryRequirements), Bonus = GunRequirements(order.bonusRequirements)
            };
        }

        private static List<GunRequirement> GunRequirements(List<GunOrderRequirement> requirements)
            => (requirements ?? new List<GunOrderRequirement>()).Where(r => r != null).Select(r => new GunRequirement
            {
                Description = Plain(r.GetDisplayName()), Tag = r.id, Value = r.value,
                Comparison = r.isCaliber ? "=" : r.GetCompareSymbol(), BaseReward = r.reward
            }).ToList();

        private static List<string> Copy(List<string> values) => values?.ToList() ?? new List<string>();
        private static string Plain(string text) => text == null ? null : Regex.Replace(text, "<[^>]*>", "");

        private static string DescribeIntent(StoreClient.ClientIntent intent)
        {
            switch (intent)
            {
                case StoreClient.ClientIntent.BUY: return T("客人来购买店主的物品（店主卖货）。");
                case StoreClient.ClientIntent.SELL: return T("客人来向店主出售物品（店主买货）。");
                case StoreClient.ClientIntent.SELLNBUY: return T("客人既买店主的物品，也向店主卖货。");
                case StoreClient.ClientIntent.BARTER: return T("易货：查看 barter 的具体交换条件。");
                case StoreClient.ClientIntent.PROCUREMENT_OFFER: return T("提出采购委托：查看 procurement 的需求与报酬。");
                case StoreClient.ClientIntent.PROCUREMENT_COLLECT: return T("采购委托交付：查看 procurement 的交货需求。");
                case StoreClient.ClientIntent.GUNSMITH: return T("枪械委托：查看 gun_order 的必需与加分条件。");
                case StoreClient.ClientIntent.INSPECTION: return T("检查来访。");
                case StoreClient.ClientIntent.RENT: return T("租金来访。");
                case StoreClient.ClientIntent.LOAN_SHARK: return T("借贷相关来访。");
                case StoreClient.ClientIntent.INFORMATION_DEALER: return T("情报商来访。");
                case StoreClient.ClientIntent.WHOLESALE: return T("批发交易来访。");
                case StoreClient.ClientIntent.APPRAISAL_SERVICE: return T("鉴定服务来访。");
                case StoreClient.ClientIntent.EXPEDITION: return T("探险相关来访。");
                case StoreClient.ClientIntent.DIALOGUE: return T("对话来访：以当前显示的对话为准。");
                default: return T("特殊或未指定来意：以当前对话和具体需求为准，不推断购买意愿。");
            }
        }
    }
}
