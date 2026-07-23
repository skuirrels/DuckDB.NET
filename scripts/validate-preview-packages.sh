#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <package-directory> <preview-version>" >&2
  exit 2
fi

package_directory=$(cd "$1" && pwd)
preview_version=$2
data_package="$package_directory/Skuirrels.DuckDB.NET.Data.Full.$preview_version.nupkg"
bindings_package="$package_directory/Skuirrels.DuckDB.NET.Bindings.Full.$preview_version.nupkg"

[[ -f "$data_package" ]] || { echo "Missing $data_package" >&2; exit 1; }
[[ -f "$bindings_package" ]] || { echo "Missing $bindings_package" >&2; exit 1; }

validation_directory=$(mktemp -d)
trap 'rm -rf "$validation_directory"' EXIT

unzip -p "$data_package" '*.nuspec' > "$validation_directory/data.nuspec"
unzip -p "$bindings_package" '*.nuspec' > "$validation_directory/bindings.nuspec"
unzip -Z1 "$bindings_package" > "$validation_directory/bindings-files.txt"

grep -Fq '<id>Skuirrels.DuckDB.NET.Data.Full</id>' "$validation_directory/data.nuspec"
grep -Fq "<version>$preview_version</version>" "$validation_directory/data.nuspec"
grep -Fq "<dependency id=\"Skuirrels.DuckDB.NET.Bindings.Full\" version=\"$preview_version\"" "$validation_directory/data.nuspec"
grep -Fq '<id>Skuirrels.DuckDB.NET.Bindings.Full</id>' "$validation_directory/bindings.nuspec"
grep -Fq "<version>$preview_version</version>" "$validation_directory/bindings.nuspec"
grep -Fq 'https://github.com/skuirrels/DuckDB.NET' "$validation_directory/data.nuspec"
grep -Fq 'https://github.com/skuirrels/DuckDB.NET' "$validation_directory/bindings.nuspec"

for native_asset in \
  runtimes/win-x64/native/duckdb.dll \
  runtimes/win-arm64/native/duckdb.dll \
  runtimes/linux-x64/native/libduckdb.so \
  runtimes/linux-arm64/native/libduckdb.so \
  runtimes/osx/native/libduckdb.dylib
do
  grep -Fxq "$native_asset" "$validation_directory/bindings-files.txt"
done

smoke_directory="$validation_directory/smoke"
mkdir -p "$smoke_directory"

cat > "$smoke_directory/PreviewSmoke.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Skuirrels.DuckDB.NET.Data.Full" Version="[$preview_version]" />
  </ItemGroup>
</Project>
EOF

cat > "$smoke_directory/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="preview" value="$package_directory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="preview">
      <package pattern="Skuirrels.DuckDB.NET.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

cat > "$smoke_directory/Program.cs" <<'EOF'
using DuckDB.NET.Data;

using var connection = new DuckDBConnection("Data Source=:memory:");
connection.Open();

using (var command = connection.CreateCommand())
{
    command.CommandText = "CREATE TABLE preview_smoke(value INTEGER)";
    command.ExecuteNonQuery();
}

using (var appender = connection.CreateAppender("preview_smoke"))
{
    appender.AppendRowScoped(42,
        static (ref DuckDBAppenderRowWriter writer, int value) => writer.AppendValue(value));
}

using var verification = connection.CreateCommand();
verification.CommandText = "SELECT version(), sum(value) FROM preview_smoke";
using var reader = verification.ExecuteReader();
if (!reader.Read() || !reader.GetString(0).Contains("v1.5.4", StringComparison.Ordinal) || reader.GetInt64(1) != 42)
{
    throw new InvalidOperationException("Preview package smoke test failed.");
}

Console.WriteLine($"Preview smoke passed with {reader.GetString(0)}");
EOF

dotnet restore "$smoke_directory/PreviewSmoke.csproj" \
  --configfile "$smoke_directory/NuGet.config" \
  --packages "$validation_directory/packages"
dotnet run --project "$smoke_directory/PreviewSmoke.csproj" --configuration Release --no-restore

echo "Validated preview packages at version $preview_version"
