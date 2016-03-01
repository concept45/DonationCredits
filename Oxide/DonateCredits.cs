﻿// Reference: Oxide.Ext.MySql
// Reference: Oxide.Ext.SQLite
using UnityEngine;
using System.Collections.Generic;
using System;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using System.Linq;
using Rust;
using System.Text;

using Oxide.Ext.SQLite;

namespace Oxide.Plugins
{
    [Info("DonateCredits", "OldeTobeh", "1.0.3")]
    [Description("Web based donation rewards, players can purchase rewards on website or in-game.")]
    
    class DonateCredits : RustPlugin
    {
        [PluginReference]
        private Plugin Economics;
        private Dictionary<ulong, double> Balances = new Dictionary<ulong, double>();
        private readonly Ext.MySql.Libraries.MySql _mySql = Interface.GetMod().GetLibrary<Ext.MySql.Libraries.MySql>();
        private Ext.MySql.Connection _mySqlConnection = null;
        public Dictionary<ulong, Dictionary<string, long>> playerList = new Dictionary<ulong, Dictionary<string, long>>();
        bool packetSent = false;
        private Timer _timer;
        
        // Do NOT edit this file, instead edit DonateCredits.json in oxide/config and DonateCredits.en.json in oxide/lang,
        // or create a language file for another language using the 'en' file as a default.
        
        #region Localization

        void LoadDefaultMessages()
        {
            var messages = new Dictionary<string, string>
            {
                {"CreditInfoChatCommand", "credits"},
                {"CreditRefreshChatCommand", "refreshcredits"},
                {"CreditGetChatCommand", "getcredits"},
                {"LoadAllPlayerCreditChatCommand", "loadcredits"},
                {"SaveAllPlayerCreditChatCommand", "savecredits"},
                {"CommandUsage", "<color=green>Donation Reward Commands:</color>\n<color=orange>/credits</color> - Displays your current donation rewards.\n<color=orange>/getcredits</color> - Gives you your current donation reward.\n<color=orange>/refreshcredits</color> - Reloads your donation rewards or you can simply reconnect."},
                {"RefreshCredits", "Shop Credits have been reloaded, type <color=orange>/credits</color> to see your Credit summary."},
                {"CreditAddFail", "You currently have no donation rewards.\nDonate today for Shop Credits on our website: <color=yellow>my.website.com</color>"},
                {"CreditAddSuccess", "You have been awarded <color=lime>${amount}.00</color> Shop Credits!!\nThank you so much for donating {playername}!!"},
                {"CreditInfo", "Donation Info for player: <color=cyan>{playername}</color>\n-> Donation Credits available: <color=lime>${amount}.00</color>\n-> Total Credits Claimed: <color=lime>${total_amount}.00</color>"},
                {"CreditInfoNoCredits", "Sorry, you don't seem to have any available donation rewards. Visit <color=yellow>my.website.com</color> to make a donation."},
                {"CreditInfoCreditsAvailable", "You have a donation reward waiting for you!\nType <color=orange>/getcredits</color> to claim your reward!"},
                {"LoadAllPlayers", "All players donation credits have been reloaded."},
                {"SaveAllPlayers", "All players donation credits have been saved."}
            };
            lang.RegisterMessages(messages, this);
        }

        #endregion
        
        #region Configuration
        
        string Address => GetConfig("address", "127.0.0.1");
        int Port => GetConfig("port", 3306);
        string dbName => GetConfig("db_name", "my_dbname");
        string User => GetConfig("user", "username");
        string Password => GetConfig("password", "password");
        
        protected override void LoadDefaultConfig()
        {
            Config["address"] = Address;
            Config["port"] = Port;
            Config["db_name"] = dbName;
            Config["user"] = User;
            Config["password"] = Password;
            SaveConfig();
        }
        
        #endregion
        
        #region Initialization
        
        private void StartConnection()
        {
            if (usingMySQL() && _mySqlConnection == null)
            {
                _mySqlConnection = _mySql.OpenDb(
                    Address, 
                    Port,
                    dbName, 
                    User,
                    Password, 
                    this);
                Puts("Connection opened.(MySQL)");
            }
        }
        
        #endregion
        
        #region DonateCredits
        
        public void setPointsAndLevel(ulong steam_id, string skill, long quantity)
        {
            if (!playerList.ContainsKey(steam_id))
                playerList.Add(steam_id, new Dictionary<string, long>());
              
            setPlayerData(steam_id, skill, quantity);  //1200 BPs = 1 Library + 800 for donating (not yet functioning, 400 is default) (300 BPs = 1 Book)
              
        }
        
        public void loadUser(BasePlayer player)
        {
            packetSent = false;
            Dictionary<string, long> statsInit = new Dictionary<string, long>();
            statsInit.Add("amount", 0);
            statsInit.Add("total_amount", 0);

            if(usingMySQL())
            {
                var sql = Ext.MySql.Sql.Builder.Append("SELECT * FROM Donations WHERE steam_id = @0", player.userID);
                _mySql.Query(sql, _mySqlConnection, list =>
                {
                    initPlayer(player, statsInit, list);
                });
            }
            packetSent = true;
        }
        
        void initPlayer(BasePlayer player, Dictionary<string, long> statsInit, List<Dictionary<string, object>> sqlData)
        {

            bool needToSave = true;
            Dictionary<string, long> tempElement = new Dictionary<string, long>();
            if (sqlData.Count > 0)
            {
                
                foreach (string key in statsInit.Keys)
                {
                    if(sqlData[0][key] != DBNull.Value)
                        tempElement.Add(key, Convert.ToInt64(sqlData[0][key]));
                }
                needToSave = false;
            }
            
            foreach (var tempItem in tempElement){
              statsInit[tempItem.Key] = tempItem.Value;
            }
                
            
            initPlayerData(player, statsInit);
            
            if (needToSave)
            {
                saveUser(player);
                Puts(player.displayName +" Saved");
            }

            //RenderUI(player);
        }
        
        void setPlayerData(ulong steam_id, string key, long value)
        {
            if (playerList[steam_id].ContainsKey(key))
                playerList[steam_id][key] = value;
            else
                playerList[steam_id].Add(key, value);
        }
        
        
        void initPlayerData(BasePlayer player, Dictionary<string, long> playerData)
        {
            foreach (var dataItem in playerData)
            {
              setPointsAndLevel(player.userID, dataItem.Key, dataItem.Value);
            }
        }
        
        public void saveUser(BasePlayer player)
        {
            if (!playerList.ContainsKey(player.userID))
            {
                Puts("Trying to save player, who haven't been loaded yet? Player name: " + player.displayName);
                return;
            }

            Dictionary<string, long> statsInit = getConnectedPlayerDetailsData(player.userID);

            string user = EncodeNonAsciiCharacters(player.displayName);
            //string user = player.displayName;
            string sqlText =
                "REPLACE INTO Donations (steam_id, user, amount, total_amount) " +
                "VALUES (@0, @1, @2, @3)";

            if (usingMySQL())
            {
                var sql = Ext.MySql.Sql.Builder.Append(sqlText, 
                    player.userID, //0
                    user, //1
                    statsInit["amount"], //2
                    statsInit["total_amount"]); //3
                _mySql.Insert(sql, _mySqlConnection, list =>
                {
                    if (list == 0) // Save to DB failed.
                        Puts("OMG WE DIDN'T SAVED IT!: " + sql.SQL);
                });
            }
        }
        
        public void rewardUser(BasePlayer player)
        {
            if (!playerList.ContainsKey(player.userID))
            {
                Puts("Trying to reward player, who haven't been loaded yet? Player name: " + player.displayName);
                return;
            }

            Dictionary<string, long> statsInit = getConnectedPlayerDetailsData(player.userID);

            string user = EncodeNonAsciiCharacters(player.displayName);
            string sqlText =
                "REPLACE INTO Donations (steam_id, user, amount, total_amount) " +
                "VALUES (@0, @1, 0, @2+@3)";
                
            //Econ Reward
            DepositPlayerMoney(player, statsInit["amount"]);
            
            if (usingMySQL())
            {
                var sql = Ext.MySql.Sql.Builder.Append(sqlText, 
                    player.userID, //0
                    user, //1
                    statsInit["amount"], //2
                    statsInit["total_amount"]); //3
                _mySql.Insert(sql, _mySqlConnection, list =>
                {
                    if (list == 0) // Save to DB failed.
                        Puts("OMG WE DIDN'T SAVED IT!: " + sql.SQL);
                });
            }
            
            //Reload Player
            reloadUser(player);
        }
        
        #endregion
        
        #region Helper Methods
        
        bool usingMySQL()
        {
            return Convert.ToBoolean("True");
        }
        
        public Dictionary<string, long> getConnectedPlayerDetailsData(ulong steam_id)
        {
            if (!playerList.ContainsKey(steam_id))
                return null;
              
            Dictionary<string, long> statsInit = new Dictionary<string, long>();
            statsInit.Add("amount", playerList[steam_id]["amount"]);
            statsInit.Add("total_amount", playerList[steam_id]["total_amount"]);
            return statsInit;
        }
        
        static string EncodeNonAsciiCharacters(string value)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in value)
            {
                if (c > 127)
                {
                    // This character is too big for ASCII
                    string encodedValue = "";
                    sb.Append(encodedValue);
                }
                else {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
        
        [HookMethod("SendHelpText")]
        private void SendHelpText(BasePlayer player)
        {
            PrintToChat(player, GetMessage("CommandUsage", player.UserIDString));
        }
        
        private bool HasAccess(BasePlayer player)
        {
            return player.net?.connection?.authLevel >= 2;
        }
        
        bool HasPermission(string userId, string perm) => permission.UserHasPermission(userId, perm);
        
        public void saveUsers()
        {
            foreach (var user in BasePlayer.activePlayerList)
            {
                saveUser(user);
            }
        }
        
        public void loadUsers()
        {
            foreach (var user in BasePlayer.activePlayerList)
            {
                reloadUser(user);
            }
            packetSent = false;
        }

        string GetFormattedMoney(BasePlayer player)
        {
            string s = string.Format("{0:C}", (double)Economics?.Call("GetPlayerMoney", player.userID));
            s = s.Substring(1);
            s = s.Remove(s.Length - 3);
            return s;
        }
        
        private void DepositPlayerMoney(BasePlayer player, double money)
        {
            Economics?.Call("Deposit", player.userID, money);
        }
        
        private bool inPlayerList(UInt64 userID)
        {
            return playerList.ContainsKey(userID);

        }
        
        T GetConfig<T>(string name, T defaultValue)
        {
            if (Config[name] == null) return defaultValue;
            return (T)Convert.ChangeType(Config[name], typeof(T));
        }
        
        string GetMessage(string key, string steamId = null) => lang.GetMessage(key, this, steamId);
        
        #endregion
        
        
        //PLUGIN HOOKS

        private void Loaded()
        {
            LoadDefaultConfig();
            LoadDefaultMessages();
            StartConnection();
            loadUsers();
            
            cmd.AddChatCommand(GetMessage("CreditInfoChatCommand"), this, "DonationsCommand");
            cmd.AddChatCommand(GetMessage("CreditRefreshChatCommand"), this, "RefreshCommand");
            cmd.AddChatCommand(GetMessage("CreditGetChatCommand"), this, "DonationRewardCommand");
            cmd.AddChatCommand(GetMessage("LoadAllPlayerCreditChatCommand"), this, "LoadCommand");
            cmd.AddChatCommand(GetMessage("SaveAllPlayerCreditChatCommand"), this, "SaveCommand");
            
            permission.RegisterPermission("donatecredits.load", this);
            permission.RegisterPermission("donatecredits.save", this);
        }
        
        private void Unload()
        {
            if (_mySqlConnection != null)
                _mySqlConnection = null;
        }
        
        void OnPlayerInit(BasePlayer player)
        {
            loadUser(player);
            Puts("Loaded Credits for "+ player.displayName +"!!");
            packetSent = false;
        }
        
        void OnPlayerDisconnected(BasePlayer player)
        {
            if (inPlayerList(player.userID))
            {
                if (playerList.ContainsKey(player.userID))
                    playerList.Remove(player.userID);
            }
            Puts(player.displayName +" Unloaded.");
        }
        
        void reloadUser(BasePlayer player) {
            if (inPlayerList(player.userID))
            {
                if (playerList.ContainsKey(player.userID))
                    playerList.Remove(player.userID);
            }
            loadUser(player);
        }
        
        //CHAT COMMANDS
        void LoadCommand(BasePlayer player, string command, string[] args)
        {
            if (HasAccess(player) || HasPermission(player.UserIDString, "donatecredits.save"))
            {
                loadUsers();
                PrintToChat(player, GetMessage("LoadAllPlayers", player.UserIDString));
            } else {
                SendReply(player, "NoPermission");
                return;
            }
        }
        
        void SaveCommand(BasePlayer player, string command, string[] args)
        {
            if (HasAccess(player) || HasPermission(player.UserIDString, "donatecredits.save"))
            {
                saveUsers();
                PrintToChat(player, GetMessage("SaveAllPlayers", player.UserIDString));
            } else {
                SendReply(player, "NoPermission");
                return;
            }
        }
        
        void RefreshCommand(BasePlayer player, string command, string[] args)
        {
            reloadUser(player);
            PrintToChat(player, GetMessage("RefreshCredits", player.UserIDString));
            packetSent = false;
        }
        
        void DonationRewardCommand(BasePlayer player, string command, string[] args)
        {
            Puts("PacketInit." + packetSent);
            reloadUser(player);
            
            _timer = timer.Repeat(1, 0, () => {
                Puts("PacketSent." + packetSent);
                if (player != null && packetSent == true) {
                    var playername = player.displayName;
                    var playerData = getConnectedPlayerDetailsData(player.userID);
                    int amount = (int)playerData["amount"];
                    
                    packetSent = false;
                    
                    if(amount == 0){
                      PrintToChat(player, GetMessage("CreditAddFail", player.UserIDString));
                      _timer.Destroy();
                      return;
                    } else {
                      PrintToChat(player, GetMessage("CreditAddSuccess", player.UserIDString).Replace("{amount}", amount.ToString()).Replace("{playername}", playername.ToString()));
                      rewardUser(player);
                      _timer.Destroy();
                    }
                }
            });
        }
        
        void OnTimer()
        {
            Puts("Tick tock!");
            _timer.Destroy();
        }
        
        void DonationsCommand(BasePlayer player, string command, string[] args)
        {
            Puts("PacketInit." + packetSent);
            reloadUser(player);
            
            _timer = timer.Repeat(1, 0, () => {
                Puts("PacketSent." + packetSent);
                if (player != null && packetSent == true)
                {
                    var playername = player.displayName;
                    var playerData = getConnectedPlayerDetailsData(player.userID);
                    int amount = (int)playerData["amount"];
                    int total_amount = (int)playerData["total_amount"];
                    
                    packetSent = false;
                    
                    if(playerData == null)
                        Puts("PlayerData IS NULL!!!");
                    
                    PrintToChat(player, GetMessage("CreditInfo", player.UserIDString).Replace("{amount}", amount.ToString()).Replace("{total_amount}", total_amount.ToString()).Replace("{playername}", playername.ToString()));
                    
                    if (amount > 0)
                        PrintToChat(player, GetMessage("CreditInfoCreditsAvailable", player.UserIDString));
                    if (amount == 0)
                        PrintToChat(player, GetMessage("CreditInfoNoCredits", player.UserIDString));
                    
                    _timer.Destroy();
                }
            });
        }
        
    }
}
