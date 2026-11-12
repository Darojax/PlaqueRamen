using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace PlaqueRamen;

internal static class Compatibility
{
    internal static void Inspect(string gameAssembly)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(gameAssembly);
        var module = assembly.MainModule;
        var view = module.GetType("UI.ViewController_Infos.Scripts.ViewController_Kenbaiki")
            ?? throw new InvalidDataException("The ticket-machine controller is missing.");
        RequireMethod(view, "InitButtonsGrid", "UnityEngine.Rendering.HighDefinition.DecalProjector[]", "System.Boolean");
        RequireMethod(view, "SetDecalRecipe", "System.Int32", "System.Int32");
        RequireMethod(view, "ClearRecipeButton");
        RequireMethod(module.GetType("Runtime_Classes.Pickups.Decoration"), "Start");
        RequireMethod(module.GetType("RSServiceLocator.Scripts.DecorationService"), "PutDownDeco");
        var interactable = module.GetType("Runtime_Classes.Common.RuntimeInteractable");
        if (interactable == null || !interactable.Methods.Any(m => m.Name == "get_IsHeld" && m.IsPublic && !m.IsStatic
            && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Boolean" && m.HasBody))
            throw new InvalidDataException("Decoration placement state contract changed.");
        RequireField(view, "SelectedRecipeIndexDict", "RecipeIndexDict");
        RequireField(module.GetType("UI.Data_Scripts.OrderData"), "price", "System.Int32");
        RequireField(module.GetType("UI.DataObjectDisplayUI"), "GUID", "SerializableGuid");
        var identity = module.GetType("SerializableGuid");
        if (identity == null || !identity.IsValueType || !identity.Methods.Any(m => m.Name == "ToString" && m.IsPublic
            && !m.IsStatic && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.String" && m.HasBody))
            throw new InvalidDataException("Recipe identity contract changed.");
        var map = module.GetType("RecipeIndexDict");
        var genericMap = module.GetType("UnitySerializedDictionary`2");
        if (map?.BaseType?.GetElementType().FullName != "UnitySerializedDictionary`2"
            || genericMap?.BaseType?.GetElementType().FullName != "System.Collections.Generic.Dictionary`2")
            throw new InvalidDataException("Ticket-machine menu dictionary contract changed.");
        if (module.GetType("UI.Data_Scripts.RamenOrderData")?.BaseType?.FullName != "UI.Data_Scripts.OrderData")
            throw new InvalidDataException("Ramen recipe type changed.");
    }

    private static void RequireMethod(TypeDefinition? type, string name, params string[] parameters)
    {
        if (type == null || type.Methods.Count(m => m.Name == name && !m.IsStatic && m.ReturnType.FullName == "System.Void"
            && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters) && m.HasBody) != 1)
            throw new InvalidDataException("Required game hook changed: " + name);
    }

    private static void RequireField(TypeDefinition? type, string name, string value)
    {
        if (type == null || type.Fields.Count(f => f.Name == name && f.IsPublic && !f.IsStatic && f.FieldType.FullName == value) != 1)
            throw new InvalidDataException("Ticket-machine field changed: " + name);
    }
}
