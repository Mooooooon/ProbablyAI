using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ProbablyAI.Hands;
using TMPro;
using UnityEngine;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Documents
{
    // Read native note/book/guide text through the shared refreshed item tree, without opening UI.
    public sealed class GameDocumentBackend : IDocumentBackend
    {
        private readonly GameHandsBackend hands;
        public GameDocumentBackend(GameHandsBackend hands) => this.hands = hands;

        public DocumentContent Read(string itemId)
        {
            if (!HealthData.CanRead())
                throw new HandsException("cannot_read", T("当前状态无法阅读文字，请先恢复健康或解除文盲状态。"));
            var world = hands.Capture();
            var info = world.Items.FirstOrDefault(i => i.Id == itemId);
            if (info == null) throw new HandsException("item_not_found", T("物品已不在当前容器树中。"));
            var item = hands.ResolveCurrentItem(itemId);
            return Note(item, info) ?? Book(item, info) ?? Slip(item, info) ?? Guide(item, info)
                ?? throw new HandsException("not_readable", T("这个物品没有可读取的文字内容，或当前版本不支持该文档类型。"));
        }

        private static DocumentContent Note(GameItem item, ItemInfo info)
        {
            if (!item.IsTag("TUTORIAL_PAGE_TITLE") && !item.IsTag("TUTORIAL_PAGE_BODY")) return null;
            return new DocumentContent
            {
                Item = info, Kind = "mentor_note", Title = Tag(item, "TUTORIAL_PAGE_TITLE") ?? info.Name,
                Body = Tag(item, "TUTORIAL_PAGE_BODY"), Signature = Tag(item, "TUTORIAL_PAGE_SIGNATURE"),
                Note = T("只读读取笔记文字，未打开界面、翻页或改变物品。")
            };
        }

        private static DocumentContent Book(GameItem item, ItemInfo info)
        {
            if (!item.IsTag("BOOK_TAG") || !item.IsTag("BOOK_ID")) return null;
            string id = item.GetTagReadonly("BOOK_ID").GetString();
            if (string.IsNullOrWhiteSpace(id)) return null;
            StoreBookData data;
            try { data = StoreBookData.GetBookById(id); }
            catch (Exception) { return null; } // Some guide items carry BOOK_TAG but no StoreBookData rows.
            if (data == null) return null;
            var result = new DocumentContent
            {
                Item = info, Kind = "book", Title = Plain(data.title) ?? info.Name,
                Note = T("只读读取书籍页数据，未翻页或改变物品。")
            };
            int count = data.pageCount > 0 ? data.pageCount :
                Math.Min(data.subtitle?.Length ?? 0, data.body?.Length ?? 0);
            for (int index = 0; index < count; index++)
                result.Pages.Add(new DocumentPage
                {
                    Number = index + 1, Subtitle = Plain(At(data.subtitle, index)), Body = Plain(At(data.body, index))
                });
            return result.Pages.Count == 0 ? null : result;
        }

        private static DocumentContent Slip(GameItem item, ItemInfo info)
        {
            if (!item.IsTag("SLIP_TITLE") && !item.IsTag("SLIP_BODY")) return null;
            return new DocumentContent
            {
                Item = info, Kind = "slip", Title = Tag(item, "SLIP_TITLE") ?? info.Name,
                Body = Tag(item, "SLIP_BODY"), Note = T("只读读取单据文字，未改变物品。")
            };
        }

        private static DocumentContent Guide(GameItem item, ItemInfo info)
        {
            if (!TryGuideRoot(item.identifier, out var root, out bool paged) || root == null) return null;
            var result = new DocumentContent
            {
                Item = info, Kind = "guide", Title = info.Name,
                Note = T("只读提取指南 / 纸面界面文字；可能包含标题、图注和界面标签，未打开窗口、翻页或改变物品。")
            };
            if (paged)
            {
                for (int index = 0; index < root.transform.childCount; index++)
                {
                    string body = UiText(root.transform.GetChild(index).gameObject);
                    if (!string.IsNullOrWhiteSpace(body))
                        result.Pages.Add(new DocumentPage { Number = index + 1, Body = body });
                }
            }
            if (result.Pages.Count == 0) result.Text = UiText(root);
            return string.IsNullOrWhiteSpace(result.Text) && result.Pages.Count == 0 ? null : result;
        }

        private static bool TryGuideRoot(string identifier, out GameObject root, out bool paged)
        {
            root = null; paged = false;
            var paper = PaperUIManager.Instance;
            switch (identifier)
            {
                case "cigarette_guide": root = paper?.cigaretteGuidePanel; return root != null;
                case "cigarette_color": root = paper?.cigaretteColorPanel; return root != null;
                case "stamp_guide": root = paper?.foodStampGuidePanel; return root != null;
                case "injector_guide": root = paper?.injectorGuidePanel; return root != null;
                case "wanted_paper": root = paper?.wantedPanel; return root != null;
                case "lottery_info": root = paper?.lotteryInfo; return root != null;
                case "aug_id_guide": root = paper?.augIdGuide; return root != null;
            }
            int bookIndex;
            switch (identifier)
            {
                case "fuel_guide": bookIndex = 0; break;
                case "water_guide": bookIndex = 1; break;
                case "hydroponic_guide": bookIndex = 2; break;
                case "chemistry_guide": bookIndex = 3; break;
                case "ribwich_guide": bookIndex = 4; break;
                case "ingot_book": bookIndex = 5; break;
                case "hand_guide": bookIndex = 6; break;
                case "wine_book": bookIndex = 7; break;
                default: return false;
            }
            var multi = MultiPageUIManager.Instance;
            if (multi?.books == null || bookIndex >= multi.books.Count) return false;
            root = multi.books[bookIndex]; paged = true;
            return root != null;
        }

        private static string UiText(GameObject root)
        {
            if (root == null) return null;
            var lines = new List<string>();
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                string value = Plain(text.text);
                if (!string.IsNullOrWhiteSpace(value) && !lines.Contains(value)) lines.Add(value);
            }
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        private static string Tag(GameItem item, string tag) => item.IsTag(tag) ? Plain(item.GetTagReadonly(tag).GetString()) : null;
        private static string At(string[] values, int index) => values != null && index < values.Length ? values[index] : null;
        private static string Plain(string value) => string.IsNullOrWhiteSpace(value) ? null : Regex.Replace(value, "<[^>]*>", "").Trim();
    }
}
