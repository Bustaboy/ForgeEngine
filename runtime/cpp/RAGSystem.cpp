#include "RAGSystem.h"

#include "Entity.h"
#include "Scene.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cctype>
#include <cstddef>
#include <optional>
#include <random>
#include <sstream>
#include <string>
#include <vector>

namespace {
constexpr std::size_t kEmbeddingDim = 24U;
constexpr std::size_t kDefaultMaxEntries = 256U;
constexpr std::size_t kPerformanceMaxEntries = 96U;
constexpr float kDefaultSimilarityThreshold = 0.78F;
constexpr float kGenerationBudgetSeconds = 8.0F;

struct FlavorTemplate {
    const char* line;
    const char* activity;
    const char* location;
    float hours;
    const char* tags;
};

constexpr std::array<FlavorTemplate, 12> kSparkFlavorTemplates{{
    {"Pauses to share a local rumor before getting back to routine.", "socialize", "town", 0.20F, "spark dialog rumor"},
    {"Takes a careful break to sharpen tools and clear the mind.", "work", "work", 0.30F, "spark craft focus"},
    {"Checks on a nearby friend after noticing their mood drop.", "socialize", "town", 0.25F, "spark relationship trust"},
    {"Stops at home for a quick meal and a reset.", "eat", "home", 0.28F, "spark needs hunger"},
    {"Takes a short scenic walk to settle thoughts.", "free_time", "town", 0.22F, "spark calm narrative"},
    {"Offers practical help at the market for a few minutes.", "work", "town", 0.25F, "spark economy settlement"},
    {"Looks for a quiet corner to recover energy.", "rest", "home", 0.32F, "spark needs energy"},
    {"Joins a light conversation about today's weather.", "socialize", "town", 0.18F, "spark weather dialog"},
    {"Checks faction notice boards for new rumors.", "free_time", "town", 0.21F, "spark faction story"},
    {"Practices drills for a short confidence boost.", "work", "work", 0.24F, "spark combat readiness"},
    {"Visits a favorite landmark to reflect on recent events.", "free_time", "town", 0.27F, "spark memory recent_actions"},
    {"Brings supplies to a neighbor and returns to schedule.", "socialize", "town", 0.23F, "spark support relationship"},
}};

std::string ToLower(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}

std::vector<std::string> Tokenize(const std::string& text) {
    std::vector<std::string> tokens{};
    std::string token;
    token.reserve(24);

    for (char raw : text) {
        const unsigned char ch = static_cast<unsigned char>(raw);
        if (std::isalnum(ch) != 0 || raw == '_') {
            token.push_back(static_cast<char>(std::tolower(ch)));
            continue;
        }
        if (!token.empty()) {
            tokens.push_back(token);
            token.clear();
        }
    }

    if (!token.empty()) {
        tokens.push_back(token);
    }

    return tokens;
}

std::array<float, kEmbeddingDim> EmbedText(const std::string& text) {
    std::array<float, kEmbeddingDim> vector{};
    const std::vector<std::string> tokens = Tokenize(text);
    if (tokens.empty()) {
        return vector;
    }

    std::hash<std::string> hasher{};
    for (const std::string& token : tokens) {
        const std::size_t index = hasher(token) % kEmbeddingDim;
        vector[index] += 1.0F;
    }

    float magnitude = 0.0F;
    for (float value : vector) {
        magnitude += value * value;
    }
    magnitude = std::sqrt(magnitude);
    if (magnitude > 0.0001F) {
        for (float& value : vector) {
            value /= magnitude;
        }
    }
    return vector;
}

float CosineSimilarity(const std::vector<float>& lhs, const std::array<float, kEmbeddingDim>& rhs) {
    if (lhs.size() != kEmbeddingDim) {
        return -1.0F;
    }

    float dot = 0.0F;
    float lhs_mag = 0.0F;
    float rhs_mag = 0.0F;
    for (std::size_t i = 0; i < kEmbeddingDim; ++i) {
        dot += lhs[i] * rhs[i];
        lhs_mag += lhs[i] * lhs[i];
        rhs_mag += rhs[i] * rhs[i];
    }

    const float denominator = std::sqrt(lhs_mag * rhs_mag);
    if (denominator <= 0.0001F) {
        return -1.0F;
    }
    return dot / denominator;
}

std::string BuildQueryText(const Scene& scene, const Entity& npc) {
    std::ostringstream query;
    query << "npc " << npc.id
          << " mood " << scene.weather.dialog_tone
          << " style " << scene.world_style_guide
          << " needs hunger=" << static_cast<int>(std::round(npc.needs.hunger))
          << " energy=" << static_cast<int>(std::round(npc.needs.energy))
          << " social=" << static_cast<int>(std::round(npc.needs.social))
          << " fun=" << static_cast<int>(std::round(npc.needs.fun));

    auto relation_it = scene.relationships.find(npc.id);
    if (relation_it != scene.relationships.end()) {
        const RelationshipProfile& relationship = relation_it->second;
        query << " relation trust=" << static_cast<int>(std::round(relationship.trust))
              << " respect=" << static_cast<int>(std::round(relationship.respect))
              << " grudge=" << static_cast<int>(std::round(relationship.grudge));
    }

    const std::size_t action_count = scene.recent_actions.size();
    const std::size_t start = action_count > 5U ? action_count - 5U : 0U;
    for (std::size_t i = start; i < action_count; ++i) {
        query << " log " << scene.recent_actions[i];
    }

    return ToLower(query.str());
}

void AppendTemplate(Scene& scene, const FlavorTemplate& templ) {
    RAGCacheEntry entry{};
    entry.id = "spark_template_" + std::to_string(scene.rag.cache_generation);
    entry.text = templ.line;
    entry.activity = templ.activity;
    entry.location = templ.location;
    entry.duration_hours = templ.hours;
    entry.tags = templ.tags;

    const std::array<float, kEmbeddingDim> embedded = EmbedText(
        std::string(templ.line) + " " + templ.tags + " " + scene.world_style_guide);
    entry.embedding.assign(embedded.begin(), embedded.end());

    scene.rag.spark_cache.push_back(entry);
    ++scene.rag.cache_generation;
}

void RebuildCacheIfNeeded(Scene& scene) {
    if (!scene.rag.spark_cache.empty() && scene.rag.spark_cache.size() <= scene.rag.max_entries) {
        return;
    }

    scene.rag.spark_cache.clear();
    for (const FlavorTemplate& templ : kSparkFlavorTemplates) {
        AppendTemplate(scene, templ);
    }
}

void TrimCache(Scene& scene) {
    if (scene.rag.spark_cache.size() <= scene.rag.max_entries) {
        return;
    }

    const std::size_t overshoot = scene.rag.spark_cache.size() - scene.rag.max_entries;
    scene.rag.spark_cache.erase(scene.rag.spark_cache.begin(), scene.rag.spark_cache.begin() + static_cast<std::ptrdiff_t>(overshoot));
}

}  // namespace

void RAGSystem::EnsureDefaults(Scene& scene) {
    scene.rag.max_entries = std::clamp(scene.rag.max_entries, 16U, 2048U);
    scene.rag.similarity_threshold = std::clamp(scene.rag.similarity_threshold, 0.35F, 0.99F);
    if (scene.rag.max_entries == 0U) {
        scene.rag.max_entries = kDefaultMaxEntries;
    }
    if (scene.rag.similarity_threshold <= 0.0F) {
        scene.rag.similarity_threshold = kDefaultSimilarityThreshold;
    }

    const bool performance_mode = scene.optimization_overrides.lightweight_mode == "performance";
    if (performance_mode) {
        scene.rag.max_entries = std::min<std::uint32_t>(scene.rag.max_entries, static_cast<std::uint32_t>(kPerformanceMaxEntries));
        scene.rag.live_fallback_enabled = false;
    }

    RebuildCacheIfNeeded(scene);
    TrimCache(scene);

    const float bytes = static_cast<float>(scene.rag.spark_cache.size()) * static_cast<float>(kEmbeddingDim * sizeof(float) + 256U);
    scene.rag.cache_size_mb = bytes / (1024.0F * 1024.0F);
    scene.rag.cache_size_mb = std::clamp(scene.rag.cache_size_mb, 0.0F, 49.5F);
}

void RAGSystem::Update(Scene& scene, float dt_seconds) {
    EnsureDefaults(scene);

    if (!scene.rag.enabled) {
        return;
    }

    scene.rag.quiet_generation_accumulator += std::max(0.0F, dt_seconds);
    const bool is_quiet_moment = scene.free_will.pending_sparks.empty() && scene.rag.quiet_generation_accumulator >= kGenerationBudgetSeconds;
    if (!is_quiet_moment) {
        return;
    }

    scene.rag.quiet_generation_accumulator = 0.0F;
    if (scene.rag.spark_cache.size() >= scene.rag.max_entries) {
        return;
    }

    const std::size_t template_index = scene.rag.cache_generation % kSparkFlavorTemplates.size();
    AppendTemplate(scene, kSparkFlavorTemplates[template_index]);
    TrimCache(scene);
}

std::optional<RAGSparkDirective> RAGSystem::RetrieveSparkFlavor(Scene& scene, const Entity& npc) {
    EnsureDefaults(scene);
    if (!scene.rag.enabled || scene.rag.spark_cache.empty()) {
        ++scene.rag.cache_misses;
        scene.rag.last_source = "miss";
        return std::nullopt;
    }

    const std::array<float, kEmbeddingDim> query = EmbedText(BuildQueryText(scene, npc));
    float best_score = -1.0F;
    const RAGCacheEntry* best_entry = nullptr;
    for (const RAGCacheEntry& entry : scene.rag.spark_cache) {
        const float score = CosineSimilarity(entry.embedding, query);
        if (score > best_score) {
            best_score = score;
            best_entry = &entry;
        }
    }

    if (best_entry == nullptr || best_score < scene.rag.similarity_threshold) {
        ++scene.rag.cache_misses;
        scene.rag.last_source = "miss";
        scene.rag.last_similarity = best_score;
        return std::nullopt;
    }

    ++scene.rag.cache_hits;
    scene.rag.last_source = "rag";
    scene.rag.last_similarity = best_score;

    RAGSparkDirective directive{};
    directive.line = best_entry->text;
    directive.activity = best_entry->activity;
    directive.location = best_entry->location;
    directive.duration_hours = best_entry->duration_hours;
    return directive;
}

std::string RAGSystem::BuildDebugSummary(Scene& scene, std::uint64_t npc_id) {
    EnsureDefaults(scene);
    std::ostringstream out;
    out << "RAG npc=" << npc_id
        << " entries=" << scene.rag.spark_cache.size()
        << " size_mb=" << scene.rag.cache_size_mb
        << " hit=" << scene.rag.cache_hits
        << " miss=" << scene.rag.cache_misses
        << " fallback_calls=" << scene.rag.live_fallback_calls
        << " last_source=" << scene.rag.last_source
        << " last_similarity=" << scene.rag.last_similarity;
    return out.str();
}
