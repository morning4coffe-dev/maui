using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Windows.Storage;

namespace Microsoft.Maui.Storage
{
	class PreferencesImplementation : IPreferences
	{
		const string StoragePrefix = "__maui_preferences__";
		static readonly object Locker = new();

		IDictionary<string, object> Values =>
			ApplicationData.Current.LocalSettings.Values;

		public bool ContainsKey(string key, string sharedName)
		{
			lock (Locker)
			{
				return Values.ContainsKey(GetStorageKey(key, sharedName));
			}
		}

		public void Remove(string key, string sharedName)
		{
			lock (Locker)
			{
				Values.Remove(GetStorageKey(key, sharedName));
			}
		}

		public void Clear(string sharedName)
		{
			lock (Locker)
			{
				var prefix = GetStoragePrefix(sharedName);
				foreach (var key in Values.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
				{
					Values.Remove(key);
				}
			}
		}

		public void Set<T>(string key, T value, string sharedName)
		{
			Preferences.CheckIsSupportedType<T>();

			lock (Locker)
			{
				var storageKey = GetStorageKey(key, sharedName);
				if (value is null)
				{
					Values.Remove(storageKey);
					return;
				}

				Values[storageKey] = value switch
				{
					DateTime dateTime => dateTime.ToBinary(),
					DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
					_ => value,
				};
			}
		}

		public T Get<T>(string key, T defaultValue, string sharedName)
		{
			lock (Locker)
			{
				if (!Values.TryGetValue(GetStorageKey(key, sharedName), out var storedValue) || storedValue is null)
				{
					return defaultValue;
				}

				if (defaultValue is DateTime && storedValue is long dateTimeBinary)
				{
					return (T)(object)DateTime.FromBinary(dateTimeBinary);
				}

				if (defaultValue is DateTimeOffset &&
					storedValue is string dateTimeOffsetText &&
					DateTimeOffset.TryParse(dateTimeOffsetText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffset))
				{
					return (T)(object)dateTimeOffset;
				}

				if (storedValue is T value)
				{
					return value;
				}

				return (T)Convert.ChangeType(storedValue, typeof(T), CultureInfo.InvariantCulture);
			}
		}

		static string GetStorageKey(string key, string sharedName) =>
			GetStoragePrefix(sharedName) + key;

		static string GetStoragePrefix(string sharedName)
		{
			var containerName = sharedName ?? string.Empty;
			return $"{StoragePrefix}{containerName.Length.ToString(CultureInfo.InvariantCulture)}:{containerName}:";
		}
	}
}
