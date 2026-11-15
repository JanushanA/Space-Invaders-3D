using System;
using System.Collections.Generic;
namespace Game.Progression
{
    public enum AssemblyKind
    {
        Weapon, Ship, Module
    }

    public readonly struct RecipePart
    {
        public readonly int Position;
        public readonly int Material;
        public readonly int Count;

        public RecipePart(int position, int material, int count)
        {
            Position = position;
            Material = material;
            Count = count;
        }
    }

    public sealed class AssemblyRecipe
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Description;
        public readonly AssemblyKind Kind;
        public readonly int Output;
        public readonly int Fee;
        readonly int[] layout;
        readonly int[] totals;

        public AssemblyRecipe(string recipeId, string name, string description, AssemblyKind kind, int output, int creditCost, int[] layout, int[] totals)
        {
            Id = recipeId;
            Name = name;
            Description = description;
            Kind = kind;
            Output = output;
            Fee = creditCost;
            this.layout = layout;
            this.totals = totals;
        }

        public int Price(InventoryState state) => Fee * (Kind == AssemblyKind.Module ? state.ModuleTier(Output) + 1 : 1);

        public RecipePart[] Pattern(InventoryState state)
        {
            var result = new RecipePart[9];
            var slotCounts = new int[6];
            var assignedSlots = new int[6];

            foreach (int materialIndex in layout)
            {
                if (materialIndex >= 0)
                {
                    slotCounts[materialIndex]++;
                }
            }

            // Split each material total across its slots; earlier slots receive any remainder.
            int multiplier = Kind == AssemblyKind.Module ? state.ModuleTier(Output) + 1 : 1;

            for (int cellIndex = 0; cellIndex < 9; cellIndex++)
            {
                int materialIndex = layout[cellIndex];
                int count = 0;
                if (materialIndex >= 0)
                {
                    int totalRequired = totals[materialIndex] * multiplier;
                    count = totalRequired / slotCounts[materialIndex] + (assignedSlots[materialIndex]++ < totalRequired % slotCounts[materialIndex] ? 1 : 0);
                }

                result[cellIndex] = new RecipePart(cellIndex, count > 0 ? materialIndex : -1, count);
            }

            return result;
        }

        public bool CanReceive(InventoryState state) => Kind == AssemblyKind.Weapon ? !state.weapons.Contains(Output) : Kind == AssemblyKind.Ship ? !state.ships.Contains(Output) : state.ModuleTier(Output) < 5;

        public void Receive(InventoryState state)
        {
            if (Kind == AssemblyKind.Weapon)
            {
                state.weapons.Add(Output);
            }
            else if (Kind == AssemblyKind.Ship)
            {
                state.ships.Add(Output);
                state.ship = Output;
            }
            else
            {
                state.modules[Output]++;
                if (Output == 0)
                {
                    state.upgrade = state.modules[0];
                }
            }
        }
    }

    public static class AssemblyRecipes
    {
        public static readonly string[] ModuleNames = { "Hull reinforcement", "Drive accelerator", "Shield regulator", "Salvage tractor" };
        public static readonly string[] ModuleDescriptions = { "Armoured bulkhead module: +0.3 maximum hull per tier.", "Engine control module: +3.5% cruise speed per tier.", "Defensive regulator: 4% less incoming damage per tier.", "Recovery module: longer pickup range and improved rare-material weights." };
        public static readonly AssemblyRecipe[] All ={
            new AssemblyRecipe("weapon-1","Arc repeater","Rapid focused energy fire.",AssemblyKind.Weapon,1,75,new[]{-1,1,-1,0,2,0,-1,-1,-1},SalvageCatalog.Recipes[1]),
            new AssemblyRecipe("weapon-2","Scatter lance","Three projectiles fan across a target.",AssemblyKind.Weapon,2,150,new[]{0,-1,0,-1,1,-1,2,-1,5},SalvageCatalog.Recipes[2]),
            new AssemblyRecipe("weapon-3","Ion rail","High-speed precision rail weapon.",AssemblyKind.Weapon,3,225,new[]{0,1,0,-1,3,-1,5,2,-1},SalvageCatalog.Recipes[3]),
            new AssemblyRecipe("weapon-4","Nova driver","Heavy three-shot plasma burst.",AssemblyKind.Weapon,4,300,new[]{0,-1,0,1,3,2,-1,4,-1},SalvageCatalog.Recipes[4]),
            new AssemblyRecipe("ship-1","Vanguard","Assemble a fast interceptor hull.",AssemblyKind.Ship,1,350,new[]{0,1,0,4,-1,4,5,2,5},SalvageCatalog.ShipCost(1)),
            new AssemblyRecipe("ship-2","Aegis","Assemble an armoured heavy escort.",AssemblyKind.Ship,2,700,new[]{0,1,0,4,3,4,5,2,5},SalvageCatalog.ShipCost(2)),
            new AssemblyRecipe("module-0",ModuleNames[0],ModuleDescriptions[0],AssemblyKind.Module,0,100,new[]{0,4,0,1,2,1,0,-1,0},new[]{10,3,3,0,2,0}),
            new AssemblyRecipe("module-1",ModuleNames[1],ModuleDescriptions[1],AssemblyKind.Module,1,100,new[]{0,-1,0,5,1,5,-1,2,-1},new[]{8,4,5,0,0,4}),
            new AssemblyRecipe("module-2",ModuleNames[2],ModuleDescriptions[2],AssemblyKind.Module,2,120,new[]{-1,4,-1,1,3,1,-1,2,-1},new[]{0,5,5,2,6,0}),
            new AssemblyRecipe("module-3",ModuleNames[3],ModuleDescriptions[3],AssemblyKind.Module,3,110,new[]{1,-1,1,0,2,0,5,-1,5},new[]{8,6,4,0,0,4})
        };

        public static AssemblyRecipe Find(string recipeId)
        {
            foreach (var recipe in All)
            {
                if (recipe.Id == recipeId)
                {
                    return recipe;
                }
            }

            return null;
        }
    }

    public struct AssemblyCell
    {
        public int Material;
        public int Count;

        public AssemblyCell(int material, int count)
        {
            Material = material;
            Count = count;
        }
    }

    // The grid holds reservations, not a second inventory. Closing/clearing never refunds items.
    public sealed class CraftingBench
    {
        public readonly AssemblyCell[] Cells = new AssemblyCell[9];

        public void Clear()
        {
            Array.Clear(Cells, 0, Cells.Length);
        }

        public int Assigned(int material)
        {
            int assignedCount = 0;

            foreach (var cell in Cells)
            {
                if (cell.Material == material && cell.Count > 0)
                {
                    assignedCount += cell.Count;
                }
            }

            return assignedCount;
        }

        public bool Add(int position, int material, int count, InventoryState state)
        {
            if (position < 0 || position >= 9 || material < 0 || material >= 6 || count < 1 || count > 999999)
            {
                return false;
            }

            var existingCell = Cells[position];
            if (existingCell.Count > 0 && existingCell.Material != material)
            {
                return false;
            }

            if (state.materials[material] - Assigned(material) < count)
            {
                return false;
            }

            Cells[position] = new AssemblyCell(material, existingCell.Count + count);
            return true;
        }

        public void Remove(int position, int count)
        {
            if (position < 0 || position >= 9 || count < 1)
            {
                return;
            }

            var cell = Cells[position];
            cell.Count = Math.Max(0, cell.Count - count);
            Cells[position] = cell;
        }

        public bool Matches(InventoryState state, AssemblyRecipe recipe)
        {
            if (recipe == null)
            {
                return false;
            }

            var requiredPattern = recipe.Pattern(state);

            for (int index = 0; index < 9; index++)
            {
                if (Cells[index].Count != requiredPattern[index].Count || (Cells[index].Count > 0 && Cells[index].Material != requiredPattern[index].Material))
                {
                    return false;
                }
            }

            return true;
        }

        public bool Craft(InventoryState state, AssemblyRecipe recipe, out string message)
        {
            message = "Place the exact quantities in the recipe's positions.";
            if (!Matches(state, recipe))
            {
                return false;
            }

            if (!recipe.CanReceive(state))
            {
                message = "Already owned or maximum module tier reached.";
                return false;
            }

            // Reservations become a real inventory deduction only when the whole recipe is valid.
            int[] cost = new int[6];

            foreach (var cell in Cells)
            {
                if (cell.Count > 0)
                {
                    cost[cell.Material] += cell.Count;
                }
            }

            if (!state.CanPay(cost, recipe.Price(state)))
            {
                message = "Insufficient materials or assembly credits.";
                return false;
            }

            for (int index = 0; index < 6; index++)
            {
                state.materials[index] -= cost[index];
            }

            state.credits -= recipe.Price(state);
            recipe.Receive(state);
            Clear();
            message = recipe.Name + (recipe.Kind == AssemblyKind.Module ? " installed." : " assembled.");
            return true;
        }
    }
}
