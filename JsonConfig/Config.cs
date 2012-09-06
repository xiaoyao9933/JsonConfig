using System;
using System.Linq;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;
using System.IO;

using JsonFx;
using JsonFx.Json;
using System.Text.RegularExpressions;

namespace JsonConfig 
{
	public class Config {
		public static dynamic Default = new ConfigObject ();
		public static dynamic User = new ConfigObject ();

		/// <summary>
		/// Gets the scope. The Scope is calculated as a Merge of the User and Default settings.
		/// Once the Scope is retrieved for the first time, the merge takes place, as well as when the User
		/// setting gets updated.
		/// </summary>
		/// <value>
		/// The scope.
		/// </value>
		public static dynamic Scope {
			get {
				if (scope == null)
					scope = Merger.Merge (User, Default);
				return scope;
			}
		}
		protected static dynamic scope = null;

		static Config ()
		{
			// static C'tor, run once to check for compiled/embedded config

			// TODO scan ALL linked assemblies and merge their configs
			var assembly = System.Reflection.Assembly.GetCallingAssembly ();
			Default = GetDefaultConfig (assembly);

			// User config (provided through a settings.conf file)
			var executionPath = AppDomain.CurrentDomain.BaseDirectory;
			var userConfigFileName = "settings";

			var d = new DirectoryInfo (executionPath);
			var userConfig = (from FileInfo fi in d.GetFiles ()
				where (
					fi.FullName.EndsWith (userConfigFileName + ".conf") ||
					fi.FullName.EndsWith (userConfigFileName + ".json") ||
					fi.FullName.EndsWith (userConfigFileName + ".conf.json") ||
					fi.FullName.EndsWith (userConfigFileName + ".json.conf")
				) select fi).FirstOrDefault ();

			if (userConfig != null) {
				User = Config.ParseJson (File.ReadAllText (userConfig.FullName));
				WatchUserConfig (userConfig);
			}
			else {
				User = new NullExceptionPreventer ();
			}
		}
		protected static FileSystemWatcher userConfigWatcher;
		public static void WatchUserConfig (FileInfo info)
		{
			userConfigWatcher = new FileSystemWatcher (info.Directory.FullName);
			userConfigWatcher.NotifyFilter = NotifyFilters.LastWrite;
			userConfigWatcher.Changed += delegate {
				User = (ConfigObject) ParseJson (File.ReadAllText (info.FullName));
				// update the scope config after merge
				scope = Merger.Merge (User, Default);
			};
			userConfigWatcher.EnableRaisingEvents = true;
		}
		public static ConfigObject ApplyJsonFromFile (FileInfo file, ConfigObject config)
		{
			var overlay_json = File.ReadAllText (file.FullName);
			dynamic overlay_config = ParseJson (overlay_json);
			return Merger.Merge (overlay_config, config);
		}
		public static ConfigObject ApplyJson (string json, ConfigObject config)
		{
			dynamic parsed = ParseJson (json);
			return Merger.Merge (parsed, config);
		}
		public static ConfigObject ParseJson (string json)
		{
			var lines = json.Split (new char[] {'\n'});
			// remove lines that start with a dash # character 
			var filtered = from l in lines
				where !(Regex.IsMatch (l, @"^\s*#(.*)"))
				select l;
			
			var filtered_json = string.Join ("\n", filtered);
			
			var json_reader = new JsonReader ();
			dynamic parsed = json_reader.Read (filtered_json);
			// convert the ExpandoObject to ConfigObject before returning
			return ConfigObject.FromExpando (parsed);
		}
		// overrides any default config specified in default.conf
		public static void SetDefaultConfig (dynamic config)
		{
			Default = config;
		}
		public static void SetUserConfig (ConfigObject config)
		{
			User = config;
			// disable the watcher
			if (userConfigWatcher != null) {
				userConfigWatcher.EnableRaisingEvents = false;
				userConfigWatcher.Dispose ();
				userConfigWatcher = null;
			}
		}
		protected static dynamic GetDefaultConfig (Assembly assembly)
		{
			var dconf_json = ScanForDefaultConfig (assembly);
			if (dconf_json == null)
				return null;
			return ParseJson (dconf_json);
		}
		protected static string ScanForDefaultConfig(Assembly assembly)
		{
			if(assembly == null)
				assembly = System.Reflection.Assembly.GetEntryAssembly ();
			
			string[] res = assembly.GetManifestResourceNames ();
			
			var dconf_resource = res.Where (r =>
					r.EndsWith ("default.conf", StringComparison.OrdinalIgnoreCase) ||
					r.EndsWith ("default.conf.json", StringComparison.OrdinalIgnoreCase))
				.FirstOrDefault ();
			
			if(string.IsNullOrEmpty (dconf_resource))
				return null;
		
			var stream = assembly.GetManifestResourceStream (dconf_resource);
			string default_json = new StreamReader(stream).ReadToEnd ();
			return default_json;
		}
	}
}
