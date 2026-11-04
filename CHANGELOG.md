# Changelog

All notable changes to Object Explorer for MySQL are listed here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## Unreleased

## 0.6.1 - 2026-11-04

First version on the Visual Studio Marketplace.

### Added

- Extension icon in *Manage Extensions* and on the Marketplace.
- A full description of the extension, a README for GitHub and the Marketplace, and this changelog.

### Changed

- The release workflow attaches the VSIX to the GitHub Release only; it no longer uploads a zipped build artifact.
- The Plan tab of Explain shows costs and row estimates with at most two decimals; the tooltip keeps the exact values.
- The lowest Visual Studio version is 17.14, because the Marketplace does not accept 18.0 yet. The publisher name is now Herbert Nijkamp, as on the Marketplace.

## 0.5.0 - 2026-11-04

First release.

### Added

- **Connections**: add, edit, remove and reorder MySQL servers; passwords encrypted with Windows DPAPI or asked on connect; connection state in the tree.
- **Object tree**: databases and system databases, tables with columns, keys, constraints, triggers and indexes, views, stored procedures and functions with parameters, events; users and roles with their grants; refresh and filter.
- **Properties window** for every object.
- **Drag and drop** of names, column lists, routine calls and connection strings into the editor, with options for quoting, database prefix and separator.
- **Script and view code**: View Code, Script As (CREATE, DROP, SELECT, INSERT, UPDATE, DELETE, CALL), Script Database with or without data.
- **MySQL Query Editor**: New Query linked to a connection and database, syntax coloring, execute and cancel, results grids, Save Results As CSV or JSON, full export of a result beyond the grid's 1000 rows.
- **IntelliSense**: completion of keywords and names, parameter info, quick info.
- **Execution plans**: Explain and Explain Analyze as a tree with warnings.
- **View and edit data** with paging, sorting and filters on the server; edit, add and delete rows in tables with a primary key.
- **Object management**: create database, new objects from templates, rename and delete with the SQL shown first.
- **Users and roles**: create users and roles, change passwords, grant and revoke privileges, grant roles.
- **Dependencies** of a table, view or routine.
- **Database documentation** as Markdown (with Mermaid diagrams) or PDF (with page numbers, contents and diagrams).
- **Activity monitor** with Kill Query and Kill Connection.
