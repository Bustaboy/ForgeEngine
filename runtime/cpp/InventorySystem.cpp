#include "InventorySystem.h"

#include "EconomySystem.h"
#include "Scene.h"

#include <sstream>

namespace {
const std::vector<Recipe>& RecipeTable() {
    static const std::vector<Recipe> recipes = {
        Recipe{"Planks", {{"wood", 4}}, "planks", 2},
        Recipe{"SmallHouse", {{"planks", 6}}, "SmallHouseItem", 1},
        Recipe{"FarmPlot", {{"stone", 8}}, "FarmPlotItem", 1},
        Recipe{"StoneBricks", {{"stone", 3}}, "stone_bricks", 2},
    };
    return recipes;
}
}  // namespace

namespace InventorySystem {

bool AddItem(Inventory& inventory, const std::string& item, int amount) {
    if (item.empty() || amount <= 0) {
        return false;
    }

    inventory.items[item] += amount;
    return true;
}

bool RemoveItem(Inventory& inventory, const std::string& item, int amount) {
    if (item.empty() || amount <= 0) {
        return false;
    }

    auto it = inventory.items.find(item);
    if (it == inventory.items.end() || it->second < amount) {
        return false;
    }

    it->second -= amount;
    if (it->second <= 0) {
        inventory.items.erase(it);
    }
    return true;
}

bool HasItems(const Inventory& inventory, const std::map<std::string, int>& required_items) {
    for (const auto& [item, required_amount] : required_items) {
        const auto it = inventory.items.find(item);
        if (it == inventory.items.end() || it->second < required_amount) {
            return false;
        }
    }
    return true;
}

const Recipe* FindRecipeByName(const std::string& recipe_name) {
    for (const Recipe& recipe : RecipeTable()) {
        if (recipe.name == recipe_name) {
            return &recipe;
        }
    }
    return nullptr;
}

bool CraftRecipe(Inventory& inventory, const std::string& recipe_name, std::string& error_message) {
    const Recipe* recipe = FindRecipeByName(recipe_name);
    if (recipe == nullptr) {
        error_message = "Unknown recipe: " + recipe_name;
        return false;
    }

    if (!HasItems(inventory, recipe->inputs)) {
        error_message = "Missing required items for recipe: " + recipe_name;
        return false;
    }

    for (const auto& [item, amount] : recipe->inputs) {
        RemoveItem(inventory, item, amount);
    }
    AddItem(inventory, recipe->output_item, recipe->output_quantity);

    error_message.clear();
    return true;
}

bool TradeWithSettlementMarket(
    Scene& scene,
    const std::string& item,
    int quantity,
    bool player_buys,
    float faction_reputation,
    std::string& summary_message) {
    return EconomySystem::TradeWithMarket(scene, item, quantity, player_buys, faction_reputation, summary_message);
}

std::vector<Recipe> DefaultRecipes() {
    return RecipeTable();
}

std::string InventorySummary(const Inventory& inventory) {
    if (inventory.items.empty()) {
        return "Inventory: (empty)";
    }

    std::ostringstream output;
    output << "Inventory:";
    for (const auto& [item, amount] : inventory.items) {
        output << ' ' << item << 'x' << amount;
    }
    return output.str();
}

}  // namespace InventorySystem
