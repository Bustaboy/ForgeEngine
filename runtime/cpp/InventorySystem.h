#pragma once

#include "Entity.h"

#include <map>
#include <string>
#include <vector>

struct Scene;

struct Recipe {
    std::string name{};
    std::map<std::string, int> inputs{};
    std::string output_item{};
    int output_quantity = 0;
};

namespace InventorySystem {

bool AddItem(Inventory& inventory, const std::string& item, int amount);
bool RemoveItem(Inventory& inventory, const std::string& item, int amount);
[[nodiscard]] bool HasItems(const Inventory& inventory, const std::map<std::string, int>& required_items);
[[nodiscard]] const Recipe* FindRecipeByName(const std::string& recipe_name);
[[nodiscard]] bool CraftRecipe(Inventory& inventory, const std::string& recipe_name, std::string& error_message);
[[nodiscard]] std::vector<Recipe> DefaultRecipes();
[[nodiscard]] std::string InventorySummary(const Inventory& inventory);
[[nodiscard]] bool TradeWithSettlementMarket(
    Scene& scene,
    const std::string& item,
    int quantity,
    bool player_buys,
    float faction_reputation,
    std::string& summary_message);

}  // namespace InventorySystem
