using UnityEngine;

namespace Raccoon.Localization
{
    public class LocalizeHelper : MonoBehaviour
    {
        private const string DefaultTable = "ui_text_table";
        
        // ================== ADS ==================
        public const string ADS_COUNTDOWN = "ADS_COUNTDOWN"; // "Ads in {0}s"

        // ================== SCHEDULE ==================
        public const string TXT_WEAK_UP = "TXT_WEAK_UP";             // "Weakup 05:00"
        public const string TXT_LUNCH_BREAK = "TXT_LUNCH_BREAK";     // "Lunch break 12:00"
        public const string TXT_LUNCH_END = "TXT_LUNCH_END";         // "Lunch end 14:00"
        public const string TXT_DINNER_BREAK = "TXT_DINNER_BREAK";   //"Dinner break 19:00"
        public const string TXT_DINNER_END = "TXT_DINNER_END";       // "Dinner end 21:00"
        public const string TXT_LIGHT_OFF = "TXT_LIGHT_OFF";       // "Light off 22:00"

        // ================== COUNTDOWN / AFTER ==================
        public const string TXT_GUARD_RETURN = "TXT_GUARD_RETURN";     // "Guard returns after"
        public const string TXT_LUNCH_BREAK_AFTER = "TXT_LUNCH_BREAK_AFTER";       // "Lunch break after"
        public const string TXT_DINNER_BREAK_AFTER = "TXT_DINNER_BREAK_AFTER";     // "Dinner break in {0}"

        // ================== UPGRADE ==================
        public const string TXT_MAX_UPGRADE = "TXT_MAX_UPGRADE";                 // "Max level"
        public const string TXT_UPGRADE_TOOL_COST = "TXT_UPGRADE_TOOL_COST";     // "x {0} for this" -> paper cost
        
        // ================== WARN / ACTION ==================
        public const string WARN_ACT_CALL_ONLY_MORNING = "WARN_ACT_CALL_ONLY_MORNING"; //"Only call guard when morning"
        public const string WARN_ACT_FULL_BACK = "WARN_ACT_FULL_BACK";//"Full backpack"
        public const string WARN_ACT_NOT_ENOUGH_PAPER = "WARN_ACT_NOT_ENOUGH_PAPER";//"Not enough paper"
        public const string WARN_ACT_WAIT_FILL_WATER = "WARN_ACT_WAIT_FILL_WATER";//"Wait fill water to drink"
        
        // ================== WARN / DIG ==================
        public const string WARN_DIG_NOT_ENOUGHT_PAPER = "WARN_DIG_NOT_ENOUGHT_PAPER";//"You are not enough paper"
        public const string WARN_DIG_TIME_UP = "WARN_DIG_TIME_UP";//"Time's up! Your bet has been returned"
        public const string WARN_DIG_NEED_ENGINE_SHOT = "WARN_DIG_NEED_ENGINE_SHOT";//"You need engine to shot"
        public const string WARN_DIG_NEED_TOOL_DIG = "WARN_DIG_NEED_TOOL_DIG";//"You need tool to dig"
        public const string WARN_DIG_NEED_ENGINE_DIG = "WARN_DIG_NEED_ENGINE_DIG";//"You need engine to dig"
        public const string WARN_DIG_FAR_TO_DIG = "WARN_DIG_FAR_TO_DIG";//"Too far to dig!"
        public const string WARN_DIG_DIG_CORRECT_AREA = "WARN_DIG_DIG_CORRECT_AREA";//"You need to dig in the correct area"

        // ================== QUEST ==================
        public const string QUEST_SELL = "QUEST_SELL"; // "Sell {quest.target} {quest.process}/{quest.amount} ",
        public const string QUEST_TRADE = "QUEST_TRADE";//"Trade to get {quest.target}"
        public const string QUEST_BUY = "QUEST_BUY";//"Buy {quest.target} {quest.process}/{quest.amount}"
        public const string QUEST_DIG = "QUEST_DIG";//"Dig {quest.target} {quest.process}/{quest.amount}"
        public const string QUEST_DIG_AND_COLLECT = "QUEST_DIG_AND_COLLECT";//"Dig and collect {quest.target} {quest.process}/{quest.amount}"
        public const string QUEST_UPGRADE = "QUEST_UPGRADE";// "Upgrade {quest.target}"
        public const string QUEST_RESTORE = "QUEST_RESTORE";// "Restore {quest.target}"
        public const string QUEST_WIN = "QUEST_WIN";//"Win {quest.target} {quest.process}/{quest.amount}"
        
        // ================== WATER BOTTLE ==================
        public const string WATER_STATUS_EMPTY = "WATER_STATUS_EMPTY";
        public const string WATER_STATUS_FILLING = "WATER_STATUS_FILLING";
        public const string WATER_STATUS_FULL = "WATER_STATUS_FULL";

        public const string DAY = "DAY";

    }
}
