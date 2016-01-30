﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CM3D2.MaidFiddler.Hook;
using CM3D2.MaidFiddler.Plugin.Gui;
using CM3D2.MaidFiddler.Plugin.Utils;
using ExIni;
using UnityEngine;
using UnityInjector;
using UnityInjector.Attributes;
using Application = System.Windows.Forms.Application;

namespace CM3D2.MaidFiddler.Plugin
{
    [PluginName("Maid Fiddler"), PluginVersion(VERSION)]
    public class MaidFiddler : PluginBase
    {
        public const string CONTRIBUTORS = "denikson";
        public const string VERSION = "BETA 0.8c";
        public const string PROJECT_PAGE = "https://github.com/denikson/CM3D2.MaidFiddler";
        public const uint SUPPORTED_PATCH_MAX = 1100;
        public const uint SUPPORTED_PATCH_MIN = 1100;
        private const bool DEFAULT_USE_JAPANESE_NAME_STYLE = false;
        private const MaidOrderDirection DEFAULT_ORDER_DIRECTION = Plugin.MaidOrderDirection.Ascending;
        private const string DEFAULT_LANGUAGE_FILE = "ENG";
        private static readonly KeyCode[] DEFAULT_KEY_CODE = {KeyCode.KeypadEnter, KeyCode.Keypad0};
        private readonly List<MaidOrderStyle> DEFAULT_ORDER_STYLES = new List<MaidOrderStyle> {MaidOrderStyle.GUID};
        public MaidFiddlerGUI.MaidCompareMethod[] COMPARE_METHODS;
        private KeyHelper keyCreateGUI;

        public MaidOrderDirection CFGOrderDirection
        {
            get
            {
                IniKey value = Preferences["GUI"]["OrderDirection"];
                MaidOrderDirection orderDirection = DEFAULT_ORDER_DIRECTION;
                if (!string.IsNullOrEmpty(value.Value) && EnumHelper.TryParse(value.Value, out orderDirection, true))
                    return orderDirection;
                Debugger.WriteLine(LogLevel.Warning, "Failed to get order direction. Setting do default...");
                value.Value = EnumHelper.GetName(DEFAULT_ORDER_DIRECTION);
                SaveConfig();

                return orderDirection;
            }

            set
            {
                Preferences["GUI"]["OrderDirection"].Value = value.ToString();
                SaveConfig();
                MaidOrderDirection = (int) value;
            }
        }

        public List<MaidOrderStyle> CFGOrderStyle
        {
            get
            {
                IniKey value = Preferences["GUI"]["OrderStyle"];
                List<MaidOrderStyle> orderStyles;
                if (string.IsNullOrEmpty(value.Value))
                {
                    Debugger.WriteLine(LogLevel.Warning, "Failed to get order style. Setting do default...");
                    value.Value = EnumHelper.EnumsToString(DEFAULT_ORDER_STYLES, '|');
                    orderStyles = DEFAULT_ORDER_STYLES;
                    SaveConfig();
                }
                else
                {
                    orderStyles = EnumHelper.ParseEnums<MaidOrderStyle>(value.Value, '|');
                    if (orderStyles.Count != 0)
                        return orderStyles;
                    Debugger.WriteLine(LogLevel.Warning, "Failed to get order style. Setting do default...");
                    value.Value = EnumHelper.EnumsToString(DEFAULT_ORDER_STYLES, '|');
                    orderStyles = DEFAULT_ORDER_STYLES;
                    SaveConfig();
                }
                return orderStyles;
            }
            set
            {
                MaidCompareMethods = value.Select(o => COMPARE_METHODS[(int) o]).ToArray();
                Preferences["GUI"]["OrderStyle"].Value = EnumHelper.EnumsToString(value, '|');
                SaveConfig();
            }
        }

        public string CFGSelectedDefaultLanguage
        {
            get
            {
                string result = Preferences["GUI"]["DefaultTranslation"].Value;
                if (!string.IsNullOrEmpty(result) && Translation.Exists(result))
                    return result;
                Preferences["GUI"]["DefaultTranslation"].Value = result = DEFAULT_LANGUAGE_FILE;
                SaveConfig();
                return result;
            }
            set
            {
                if (value != null && (value = value.Trim()) != string.Empty && Translation.Exists(value))
                    Preferences["GUI"]["DefaultTranslation"].Value = value;
                else
                    Preferences["GUI"]["DefaultTranslation"].Value = DEFAULT_LANGUAGE_FILE;
                SaveConfig();
            }
        }

        public List<KeyCode> CFGStartGUIKey
        {
            get
            {
                List<KeyCode> keys = new List<KeyCode>();
                IniKey value = Preferences["Keys"]["StartGUIKey"];
                if (string.IsNullOrEmpty(value.Value))
                {
                    value.Value = EnumHelper.EnumsToString(DEFAULT_KEY_CODE, '+');
                    keys.AddRange(DEFAULT_KEY_CODE);
                    SaveConfig();
                }
                else
                {
                    keys = EnumHelper.ParseEnums<KeyCode>(value.Value, '+');

                    if (keys.Count != 0)
                        return keys;
                    Debugger.WriteLine(LogLevel.Warning, "Failed to parse given key combo. Using default combination");
                    keys = DEFAULT_KEY_CODE.ToList();
                    value.Value = EnumHelper.EnumsToString(keys, '+');
                    SaveConfig();
                }
                return keys;
            }

            set
            {
                Preferences["Keys"]["StartGUIKey"].Value = EnumHelper.EnumsToString(value, '+');
                keyCreateGUI.Keys = value.ToArray();
                SaveConfig();
            }
        }

        public bool CFGUseJapaneseNameStyle
        {
            get
            {
                IniKey value = Preferences["GUI"]["UseJapaneseNameStyle"];
                bool useJapNameStyle = DEFAULT_USE_JAPANESE_NAME_STYLE;
                if (!string.IsNullOrEmpty(value.Value) && bool.TryParse(value.Value, out useJapNameStyle))
                    return useJapNameStyle;
                Debugger.WriteLine(LogLevel.Warning, "Failed to get name style info. Setting to default...");
                value.Value = DEFAULT_USE_JAPANESE_NAME_STYLE.ToString();
                SaveConfig();
                return useJapNameStyle;
            }
            set
            {
                UseJapaneseNameStyle = value;
                Preferences["GUI"]["UseJapaneseNameStyle"].Value = value.ToString();
                SaveConfig();
            }
        }

        public static string DATA_PATH { get; private set; }
        public MaidFiddlerGUI Gui { get; set; }
        public Thread GuiThread { get; set; }
        public MaidFiddlerGUI.MaidCompareMethod[] MaidCompareMethods { get; private set; }
        public int MaidOrderDirection { get; private set; }
        public bool UseJapaneseNameStyle { get; private set; }

        public void Dispose()
        {
            Gui?.Dispose();
        }

        public void Awake()
        {
            if (!FiddlerUtils.CheckPatcherVersion())
            {
                Destroy(this);
                return;
            }
            DontDestroyOnLoad(this);

            Debugger.ErrorOccured += (exception, message) => FiddlerUtils.ThrowErrorMessage(exception, message, this);

            COMPARE_METHODS = new MaidFiddlerGUI.MaidCompareMethod[]
            {
                MaidCompareID,
                MaidCompareCreateTime,
                MaidCompareFirstName,
                MaidCompareLastName,
                MaidCompareEmployedDay
            };

            DATA_PATH = DataPath;
            LoadConfig();

            FiddlerHooks.SaveLoadedEvent += OnSaveLoaded;

            Debugger.WriteLine(LogLevel.Info, "Creating the GUI thread");
            GuiThread = new Thread(LoadGUI);
            GuiThread.Start();

            Debugger.WriteLine($"MaidFiddler {VERSION} loaded!");
        }

        public void LateUpdate()
        {
            Gui?.DoIfVisible(Gui.UpdateSelectedMaidValues);
            Gui?.DoIfVisible(Gui.UpdatePlayerValues);
        }

        private void LoadConfig()
        {
            Debugger.WriteLine(LogLevel.Info, "Loading launching key combination...");
            keyCreateGUI = new KeyHelper(CFGStartGUIKey.ToArray());
            Debugger.WriteLine(
            LogLevel.Info,
            $"Loaded {keyCreateGUI.Keys.Length} long key combo: {EnumHelper.EnumsToString(keyCreateGUI.Keys, '+')}");

            Debugger.WriteLine(LogLevel.Info, "Loading name style info...");
            UseJapaneseNameStyle = CFGUseJapaneseNameStyle;
            Debugger.WriteLine(LogLevel.Info, $"Using Japanese name style: {UseJapaneseNameStyle}");

            Debugger.WriteLine(LogLevel.Info, "Loading order style info...");
            List<MaidOrderStyle> orderStyles = CFGOrderStyle;
            MaidCompareMethods = orderStyles.Select(o => COMPARE_METHODS[(int) o]).ToArray();
            Debugger.WriteLine(
            LogLevel.Info,
            $"Sorting maids by method order {EnumHelper.EnumsToString(orderStyles, '>')}");


            Debugger.WriteLine(LogLevel.Info, "Loading order direction info...");
            MaidOrderDirection = (int) CFGOrderDirection;
            Debugger.WriteLine(
            LogLevel.Info,
            $"Sorting maids in {EnumHelper.GetName((MaidOrderDirection) MaidOrderDirection)} direction");

            Translation.LoadTranslation(CFGSelectedDefaultLanguage);
        }

        public void LoadGUI()
        {
            try
            {
                Application.SetCompatibleTextRenderingDefault(false);
                if (Gui == null)
                    Gui = new MaidFiddlerGUI(this);
                Application.Run(Gui);
            }
            catch (Exception e)
            {
                FiddlerUtils.ThrowErrorMessage(e, "Generic error", this);
            }
        }

        public void OnDestroy()
        {
            if (Gui == null)
                return;
            Debugger.WriteLine("Closing GUI...");
            Gui.Close(true);
            Gui = null;
            Debugger.WriteLine("GUI closed. Suspending the thread...");
            GuiThread.Abort();
            Debugger.WriteLine("Thread suspended");
        }

        public void OnSaveLoaded(int saveNo)
        {
            Debugger.WriteLine(LogLevel.Info, $"Level loading! Save no. {saveNo}");
            Gui?.DoIfVisible(Gui.ReloadMaids);
            Gui?.DoIfVisible(Gui.ReloadPlayer);
        }

        public void OpenGUI()
        {
            Gui?.Show();
        }

        public void Update()
        {
            keyCreateGUI.Update();

            if (keyCreateGUI.HasBeenPressed())
                OpenGUI();
            Gui?.DoIfVisible(Gui.UpdateMaids);
        }

        public int MaidCompareEmployedDay(Maid x, Maid y)
        {
            return ComputeOrder(x.Param.status.employment_day, y.Param.status.employment_day);
        }

        public int MaidCompareCreateTime(Maid x, Maid y)
        {
            return ComputeOrder(x.Param.status.create_time_num, y.Param.status.create_time_num);
        }

        private int ComputeOrder<T>(T x, T y) where T : IComparable<T>
        {
            return MaidOrderDirection * x.CompareTo(y);
        }

        public int MaidCompareFirstName(Maid x, Maid y)
        {
            return MaidOrderDirection
                   * string.CompareOrdinal(
                   x.Param.status.first_name.ToUpperInvariant(),
                   y.Param.status.first_name.ToUpperInvariant());
        }

        public int MaidCompareID(Maid x, Maid y)
        {
            return MaidOrderDirection * string.CompareOrdinal(x.Param.status.guid, y.Param.status.guid);
        }

        public int MaidCompareLastName(Maid x, Maid y)
        {
            return MaidOrderDirection
                   * string.CompareOrdinal(
                   x.Param.status.last_name.ToUpperInvariant(),
                   y.Param.status.last_name.ToUpperInvariant());
        }
    }

    public enum MaidOrderStyle
    {
        GUID = 0,
        CreationTime = 1,
        FirstName = 2,
        LastName = 3,
        EmployedDay = 4
    }

    public enum MaidOrderDirection
    {
        Descending = -1,
        Ascending = 1
    }
}