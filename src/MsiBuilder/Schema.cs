using System;

namespace StayAwake.MsiBuilder
{
    /// <summary>The standard Windows Installer tables this package uses, and its action sequences.</summary>
    static class Schema
    {
        public static readonly string[] CreateStatements =
        {
            "CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR NOT NULL LOCALIZABLE PRIMARY KEY `Property`)",
            "CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)",
            "CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)",
            "CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`)",
            "CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)",
            "CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` SHORT NOT NULL PRIMARY KEY `File`)",
            "CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` SHORT NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)",
            "CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` LONGCHAR LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)",
            "CREATE TABLE `RemoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) LOCALIZABLE, `DirProperty` CHAR(72) NOT NULL, `InstallMode` SHORT NOT NULL PRIMARY KEY `FileKey`)",
            "CREATE TABLE `ServiceInstall` (`ServiceInstall` CHAR(72) NOT NULL, `Name` CHAR(255) NOT NULL, `DisplayName` CHAR(255) LOCALIZABLE, `ServiceType` LONG NOT NULL, `StartType` LONG NOT NULL, `ErrorControl` LONG NOT NULL, `LoadOrderGroup` CHAR(255), `Dependencies` CHAR(255), `StartName` CHAR(255), `Password` CHAR(255), `Arguments` CHAR(255), `Component_` CHAR(72) NOT NULL, `Description` CHAR(255) LOCALIZABLE PRIMARY KEY `ServiceInstall`)",
            "CREATE TABLE `ServiceControl` (`ServiceControl` CHAR(72) NOT NULL, `Name` CHAR(255) NOT NULL LOCALIZABLE, `Event` SHORT NOT NULL, `Arguments` CHAR(255) LOCALIZABLE, `Wait` SHORT, `Component_` CHAR(72) NOT NULL PRIMARY KEY `ServiceControl`)",
            "CREATE TABLE `Upgrade` (`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL PRIMARY KEY `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`)",
            "CREATE TABLE `LaunchCondition` (`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Condition`)",
            "CREATE TABLE `InstallExecuteSequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)",
            "CREATE TABLE `InstallUISequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)",
        };

        public static readonly Tuple<string, string, int>[] ExecuteSequence =
        {
            T("LaunchConditions", 100),
            T("FindRelatedProducts", 200),
            T("CostInitialize", 800),
            T("FileCost", 900),
            T("CostFinalize", 1000),
            T("MigrateFeatureStates", 1200),
            T("InstallValidate", 1400),
            T("InstallInitialize", 1500),
            T("RemoveExistingProducts", 1510),   // full uninstall of the old version first
            T("ProcessComponents", 1600),
            T("UnpublishFeatures", 1800),
            T("StopServices", 1900),
            T("DeleteServices", 2000),
            T("RemoveRegistryValues", 2600),
            T("RemoveFiles", 3500),
            T("InstallFiles", 4000),
            T("WriteRegistryValues", 5000),
            T("InstallServices", 5800),
            T("StartServices", 5900),
            T("RegisterUser", 6000),
            T("RegisterProduct", 6100),
            T("PublishFeatures", 6300),
            T("PublishProduct", 6400),
            T("InstallFinalize", 6600),
        };

        public static readonly Tuple<string, string, int>[] UiSequence =
        {
            T("LaunchConditions", 100),
            T("FindRelatedProducts", 200),
            T("CostInitialize", 800),
            T("FileCost", 900),
            T("CostFinalize", 1000),
            T("MigrateFeatureStates", 1200),
            T("ExecuteAction", 1300),
        };

        static Tuple<string, string, int> T(string action, int seq) { return Tuple.Create(action, (string)null, seq); }
    }
}
