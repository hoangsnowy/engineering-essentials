using EngineeringEssentials.Demos;

// Engineering Essentials — runnable demos for the tech-sharing session.
//
//   dotnet run            -> run all chapters
//   dotnet run -- 1       -> Chapter 1 (Enum Flags & Domain Modeling)
//   dotnet run -- 2       -> Chapter 2 (Numeric Types)
//   dotnet run -- 3       -> Chapter 3 (Concurrency Control)

string choice = args.Length > 0 ? args[0] : "all";

switch (choice)
{
    case "1":
        Chapter01EnumFlags.Run();
        break;
    case "2":
        Chapter02NumericTypes.Run();
        break;
    case "3":
        Chapter03Concurrency.Run();
        break;
    default:
        Chapter01EnumFlags.Run();
        Chapter02NumericTypes.Run();
        Chapter03Concurrency.Run();
        break;
}

Console.WriteLine();
Console.WriteLine("Done. (Tip: present with `dotnet run -- 1` then `-- 2` then `-- 3`.)");
