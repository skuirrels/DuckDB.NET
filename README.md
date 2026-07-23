# DuckDB.NET - Performance fork preview

This is the [`skuirrels/DuckDB.NET`](https://github.com/skuirrels/DuckDB.NET)
fork of the [upstream DuckDB.NET project](https://github.com/Giorgi/DuckDB.NET).
It provides preview packages containing the cutting-edge performance work while
the corresponding upstream pull requests are reviewed and released.

[![NuGet (Data)](https://img.shields.io/nuget/vpre/Skuirrels.DuckDB.NET.Data.Full.svg?label=NuGet%20%28Data%29)](https://www.nuget.org/packages/Skuirrels.DuckDB.NET.Data.Full)
[![NuGet (Bindings)](https://img.shields.io/nuget/vpre/Skuirrels.DuckDB.NET.Bindings.Full.svg?label=NuGet%20%28Bindings%29)](https://www.nuget.org/packages/Skuirrels.DuckDB.NET.Bindings.Full)
[![.NET 8 and 10](https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)

## Usage

```sh
dotnet add package Skuirrels.DuckDB.NET.Data.Full --version 1.5.4-preview.2
```

The fork packages retain the official `DuckDB.NET.Data` namespaces and assembly
names. Do not reference this package and the official `DuckDB.NET.Data.Full`
package in the same dependency graph.

```cs
using (var duckDBConnection = new DuckDBConnection("Data Source=file.db"))
{
  duckDBConnection.Open();

  using var command = duckDBConnection.CreateCommand();

  command.CommandText = "CREATE TABLE integers(foo INTEGER, bar INTEGER);";
  var executeNonQuery = command.ExecuteNonQuery();

  command.CommandText = "INSERT INTO integers VALUES (3, 4), (5, 6), (7, 8);";
  executeNonQuery = command.ExecuteNonQuery();

  command.CommandText = "Select count(*) from integers";
  var executeScalar = command.ExecuteScalar();

  command.CommandText = "SELECT foo, bar FROM integers";
  var reader = command.ExecuteReader();

  PrintQueryResults(reader);
}

private static void PrintQueryResults(DbDataReader queryResult)
{
  for (var index = 0; index < queryResult.FieldCount; index++)
  {
    var column = queryResult.GetName(index);
    Console.Write($"{column} ");
  }

  Console.WriteLine();

  while (queryResult.Read())
  {
    for (int ordinal = 0; ordinal < queryResult.FieldCount; ordinal++)
    {
      var val = queryResult.GetInt32(ordinal);
      Console.Write(val);
      Console.Write(" ");
    }

    Console.WriteLine();
  }
}
```

### MotherDuck

To connect to [MotherDuck](https://motherduck.com):

```cs
using var duckDBConnection = new DuckDBConnection("DataSource=md:{your_database}?motherduck_token=ey...");
```

## DuckDB Extensions (C#)

If you want to build DuckDB extensions with C#, see [Giorgi/DuckDB.ExtensionKit](https://github.com/Giorgi/DuckDB.ExtensionKit).

## Known Issues

When debugging your project that uses DuckDB.NET library, you may get the following error: **System.AccessViolationException: Attempted to read or write protected memory. This is often an indication that other memory is corrupt**. The error happens due to debugger interaction with the native memory. For a workaround check out [Debugger Options mess up debugging session during Marshalling
](https://youtrack.jetbrains.com/issue/RIDER-114126/Debugger-Options-mess-up-debugging-session-during-Marshalling)

## Documentation

Documentation is available at [https://duckdb.net](https://duckdb.net)

## Support

For a fork-specific problem, [create an issue in this fork](https://github.com/skuirrels/DuckDB.NET/issues/new).
For upstream DuckDB.NET support, use the [upstream issue tracker](https://github.com/Giorgi/DuckDB.NET/issues/new).
You can also join the [DuckDB `dotnet` channel](https://discord.duckdb.org/) for DuckDB.NET-related topics.

## Upstream contributors

[![Contributors](https://contrib.rocks/image?repo=Giorgi/DuckDB.NET)](https://github.com/Giorgi/DuckDB.NET/graphs/contributors)

## Sponsors

A big thanks to [DuckDB Labs](https://duckdblabs.com/) and [AWS Open Source Software Fund](https://github.com/aws/dotnet-foss) for sponsoring the project!

[![DuckDB Labs](https://raw.githubusercontent.com/Giorgi/DuckDB.NET/main/.github/sponsors/duckdb-labs-logo.png)](https://duckdblabs.com/)

[![AWS](https://raw.githubusercontent.com/Giorgi/DuckDB.NET/main/.github/sponsors/aws-logo-small.png)](https://github.com/aws/dotnet-foss)
