namespace Uno.Maui.Integrity
{
	public sealed class ValidateManagedImages : Microsoft.Build.Framework.ITask
	{
		public Microsoft.Build.Framework.IBuildEngine BuildEngine { get; set; }
		public Microsoft.Build.Framework.ITaskHost HostObject { get; set; }
		[Microsoft.Build.Framework.Required]
		public Microsoft.Build.Framework.ITaskItem[] Images { get; set; }
		[Microsoft.Build.Framework.Required]
		public string LeaseKey { get; set; }
		public string PackageFile { get; set; }

		sealed class Leases : System.IDisposable
		{
			public readonly System.Collections.Generic.List<ManagedImage> Images =
				new System.Collections.Generic.List<ManagedImage>();
			public readonly System.Collections.Generic.Dictionary<string, ManagedImage> PackageImages =
				new System.Collections.Generic.Dictionary<string, ManagedImage>(System.StringComparer.Ordinal);
			public System.IO.FileStream Package;
			public void Dispose()
			{
				foreach (var image in Images)
					image.Dispose();
				if (Package != null)
					Package.Dispose();
			}
		}

		public bool Execute()
		{
			var leases = new Leases();
			bool registered = false;
			try
			{
				var engine = BuildEngine as Microsoft.Build.Framework.IBuildEngine4;
				if (engine == null)
					throw new System.InvalidOperationException("Build-lifetime input leases require IBuildEngine4.");
				if (!System.String.IsNullOrEmpty(PackageFile))
				{
					var retained = engine.GetRegisteredTaskObject(LeaseKey,
						Microsoft.Build.Framework.RegisteredTaskObjectLifetime.Build) as Leases;
					if (retained == null || retained.PackageImages.Count == 0)
						throw new System.InvalidOperationException("Package has no retained validated input snapshots.");
					VerifyPackage(retained, PackageFile);
					return true;
				}
				if (Images == null || Images.Length == 0)
					throw new System.InvalidOperationException("No managed runtime inputs were selected.");
				if (engine.GetRegisteredTaskObject(LeaseKey, Microsoft.Build.Framework.RegisteredTaskObjectLifetime.Build) != null)
					throw new System.InvalidOperationException("Managed inputs were already leased for this package.");
				foreach (var item in Images)
				{
					var image = ManagedImage.Open(item.ItemSpec, item.GetMetadata("AssemblyName"));
					leases.Images.Add(image);
					var packagePath = item.GetMetadata("PackagePath");
					if (!System.String.IsNullOrEmpty(packagePath))
						leases.PackageImages.Add(packagePath.Replace('\\', '/'), image);
					var producerPath = item.GetMetadata("ProducerReference");
					if (!System.String.IsNullOrEmpty(producerPath))
					{
						var producer = ManagedImage.Open(producerPath, item.GetMetadata("AssemblyName"));
						leases.Images.Add(producer);
						ManagedImage.AssertReferenceCopy(image, producer);
					}
					BuildEngine.LogMessageEvent(new Microsoft.Build.Framework.BuildMessageEventArgs(
						"Validated managed input " + image.Path + " SHA256=" + image.Sha256,
						null, nameof(ValidateManagedImages), Microsoft.Build.Framework.MessageImportance.Low));
				}
				// NuGet Pack reads these same paths later in this build. Do not release handles
				// on task return, or a writer could replace validated bytes before consumption.
				engine.RegisterTaskObject(LeaseKey, leases,
					Microsoft.Build.Framework.RegisteredTaskObjectLifetime.Build, false);
				registered = true;
				return true;
			}
			catch (System.Exception error)
			{
				// An integrity error is terminal, never a copy, rebuild, warning or retry.
				BuildEngine.LogErrorEvent(new Microsoft.Build.Framework.BuildErrorEventArgs(
					null, "UMRI001", null, 0, 0, 0, 0,
					"Managed artifact integrity validation failed: " + error.Message, null, nameof(ValidateManagedImages)));
				return false;
			}
			finally
			{
				if (!registered)
					leases.Dispose();
			}

			static void VerifyPackage(Leases leases, string packageFile)
			{
				var stream = new System.IO.FileStream(packageFile, System.IO.FileMode.Open,
					System.IO.FileAccess.Read, System.IO.FileShare.Read);
				try
				{
					using (var archive = new System.IO.Compression.ZipArchive(stream,
						System.IO.Compression.ZipArchiveMode.Read, true))
					{
						foreach (var pair in leases.PackageImages)
						{
							int count = 0;
							foreach (var entry in archive.Entries)
							{
								if (entry.FullName.Replace('\\', '/') != pair.Key)
									continue;
								count++;
								using (var input = entry.Open())
								using (var hash = System.Security.Cryptography.SHA256.Create())
								{
									if (entry.Length != pair.Value.Length ||
										System.BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "") != pair.Value.Sha256)
										throw new System.IO.InvalidDataException("Packaged bytes differ from validated input: " + pair.Key);
								}
							}
							if (count != 1)
								throw new System.IO.InvalidDataException("Missing or duplicated managed package entry: " + pair.Key);
						}
					}
					leases.Package = stream;
				}
				catch
				{
					stream.Dispose();
					throw;
				}
			}
		}
	}
}
