#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Windows.Security.Credentials;

namespace Microsoft.Maui.Storage
{
	partial class SecureStorageImplementation : ISecureStorage
	{
		const int ElementNotFoundHResult = unchecked((int)0x80070490);

		static string PasswordVaultResource =>
			GetSecureStoragePasswordVaultResource(AppInfo.Current.PackageName);

		Task<string?> PlatformGetAsync(string key)
		{
			var credential = FindCredential(key);
			if (credential is null)
			{
				return Task.FromResult<string?>(null);
			}

			credential.RetrievePassword();
			return Task.FromResult<string?>(credential.Password);
		}

		Task PlatformSetAsync(string key, string data)
		{
			var vault = GetPasswordVault();
			var userName = HashSecureStorageKey(key);

			RemoveCredentials(vault, userName);
			vault.Add(new PasswordCredential(PasswordVaultResource, userName, data));

			return Task.CompletedTask;
		}

		bool PlatformRemove(string key)
		{
			var vault = GetPasswordVault();
			return RemoveCredentials(vault, HashSecureStorageKey(key));
		}

		void PlatformRemoveAll()
		{
			var vault = GetPasswordVault();
			foreach (var credential in GetCredentials(vault))
			{
				vault.Remove(credential);
			}
		}

		static PasswordCredential? FindCredential(string key)
		{
			var userName = HashSecureStorageKey(key);
			foreach (var credential in GetCredentials(GetPasswordVault()))
			{
				if (string.Equals(credential.UserName, userName, StringComparison.Ordinal))
				{
					return credential;
				}
			}

			return null;
		}

		static PasswordVault GetPasswordVault()
		{
			EnsurePasswordVaultSupport();
			return new PasswordVault();
		}

		static IReadOnlyList<PasswordCredential> GetCredentials(PasswordVault vault)
		{
			try
			{
				return vault.FindAllByResource(PasswordVaultResource);
			}
			catch (Exception ex) when (ex.HResult == ElementNotFoundHResult)
			{
				return Array.Empty<PasswordCredential>();
			}
		}

		static bool RemoveCredentials(PasswordVault vault, string userName)
		{
			var removed = false;

			foreach (var credential in GetCredentials(vault))
			{
				if (!string.Equals(credential.UserName, userName, StringComparison.Ordinal))
				{
					continue;
				}

				vault.Remove(credential);
				removed = true;
			}

			return removed;
		}

		static void EnsurePasswordVaultSupport()
		{
			if (OperatingSystem.IsWindows() ||
				OperatingSystem.IsIOS() ||
				OperatingSystem.IsMacCatalyst())
			{
				return;
			}

			throw CreateUnsupportedException();
		}

		static FeatureNotSupportedException CreateUnsupportedException()
		{
			if (OperatingSystem.IsBrowser())
			{
				return new FeatureNotSupportedException("SecureStorage is not supported by the Uno WebAssembly host.");
			}

			if (OperatingSystem.IsAndroid())
			{
				return new FeatureNotSupportedException("SecureStorage is not supported by the Uno Android host.");
			}

			if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
			{
				return new FeatureNotSupportedException("SecureStorage is not supported by the Uno desktop hosts on Linux or macOS.");
			}

			return new FeatureNotSupportedException("SecureStorage is not supported by this Uno host.");
		}
	}
}
