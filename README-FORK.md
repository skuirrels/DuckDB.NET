# Skuirrels DuckDB.NET fork

This is an unofficial stable build from the
[`skuirrels/DuckDB.NET`](https://github.com/skuirrels/DuckDB.NET) fork. It
packages the consolidated performance work for DuckDB v1.5.4 under distinct
`Skuirrels.DuckDB.NET.*` package IDs.

Install the bundled provider explicitly:

```shell
dotnet add package Skuirrels.DuckDB.NET.Data.Full --version 1.5.4
```

NuGet packages:

- [`Skuirrels.DuckDB.NET.Data.Full`](https://www.nuget.org/packages/Skuirrels.DuckDB.NET.Data.Full/)
- [`Skuirrels.DuckDB.NET.Bindings.Full`](https://www.nuget.org/packages/Skuirrels.DuckDB.NET.Bindings.Full/)

The package keeps the official `DuckDB.NET.Data` namespaces and assembly names,
so application source code does not need to change. Do not reference this fork
package and the official `DuckDB.NET.Data.Full` package in the same dependency
graph because they contain assemblies with the same identities.

This release bundles DuckDB v1.5.4 and includes the consolidated appender,
parameter binding, prepared-command, result materialisation, and scoped-writer
optimisations from the fork. When equivalent upstream changes are released,
move back to the official `DuckDB.NET.Data.Full` package.

The original DuckDB.NET and DuckDB licences and attribution are included in the
package.

Report fork-specific problems in the
[`skuirrels/DuckDB.NET` issue tracker](https://github.com/skuirrels/DuckDB.NET/issues/new).
