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
constexpr std::size_t kDefaultLegacyMaxEntries = 192U;
constexpr std::size_t kPerformanceLegacyMaxEntries = 64U;
constexpr float kDefaultSimilarityThreshold = 0.78F;
constexpr float kGenerationBudgetSeconds = 8.0F;
constexpr std::uint32_t kPerformanceNarrativeStride = 3U;

struct FlavorTemplate {
    const char* line;
    const char* activity;
    const char* location;
    float hours;
    const char* tags;
};

struct NarrativeTemplate {
    const char* checkpoint;
    const char* dialog_tone;
    const char* msq_branch;
    const char* event_color;
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

constexpr std::array<NarrativeTemplate, 12> kNarrativeFlavorTemplates{{
    {"workshop_milestone", "hopeful", "msq_workshop_unity", "artisan_pride", "art_bible workshop craft community"},
    {"workshop_milestone", "determined", "msq_workshop_recovery", "forged_urgency", "art_bible workshop pressure grit"},
    {"relationship_threshold", "warm", "msq_relationship_trust", "intimate_reflection", "relationship trust loyalty hearth"},
    {"relationship_threshold", "guarded", "msq_relationship_fragile", "tense_repair", "relationship grudge debt apology"},
    {"season_change", "wistful", "msq_season_memory", "harvest_glow", "season autumn ritual memory"},
    {"season_change", "optimistic", "msq_season_new_path", "spring_bloom", "season spring renewal"},
    {"moral_choice", "solemn", "msq_moral_consequence", "quiet_judgment", "moral choice consequence justice"},
    {"moral_choice", "resolute", "msq_moral_resolve", "frontier_oath", "moral choice resolve frontier"},
    {"performance_snapshot", "focused", "msq_efficiency_push", "workshop_rhythm", "performance productivity cadence"},
    {"scripted_behavior", "playful", "msq_behavior_sync", "town_banter", "scripted behavior social spark"},
    {"major_story_beat", "cinematic", "msq_major_beat", "dramatic_contrast", "story beat narrator cadence"},
    {"generic", "grounded", "msq_default", "ambient_texture", "fallback grounded stylized frontier"},
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
    if (!scene.compressed_event_log.empty()) {
        const CompressedLegacyEvent& latest_legacy = scene.compressed_event_log.back();
        query << " legacy generation=" << latest_legacy.generation
              << " type=" << latest_legacy.event_type
              << " memory " << latest_legacy.summary;
    }

    return ToLower(query.str());
}

void AppendTemplate(Scene& scene, const FlavorTemplate& templ) {
    RAGCacheEntry entry{};
    entry.id = "spark_template_" + std::to_string(scene.rag.cache_generation);
    entry.text = templ.line;
    entry.activity = templ.activity;
    entry.location = templ.location;
    entry.checkpoint = "generic";
    entry.category = "spark";
    entry.duration_hours = templ.hours;
    entry.tags = templ.tags;

    const std::array<float, kEmbeddingDim> embedded = EmbedText(
        std::string(templ.line) + " " + templ.tags + " " + scene.world_style_guide);
    entry.embedding.assign(embedded.begin(), embedded.end());

    scene.rag.spark_cache.push_back(entry);
    ++scene.rag.cache_generation;
}

void AppendNarrativeTemplate(Scene& scene, const NarrativeTemplate& templ) {
    RAGCacheEntry entry{};
    entry.id = "narrative_template_" + std::to_string(scene.rag.cache_generation);
    entry.text = std::string("dialog_tone=") + templ.dialog_tone +
        " msq_branch=" + templ.msq_branch +
        " event_color=" + templ.event_color;
    entry.activity = "narrative";
    entry.location = "story";
    entry.checkpoint = templ.checkpoint;
    entry.category = "narrative";
    entry.tags = templ.tags;
    entry.duration_hours = 0.0F;

    const std::array<float, kEmbeddingDim> embedded = EmbedText(
        std::string(templ.checkpoint) + " " + templ.dialog_tone + " " + templ.msq_branch + " " +
        templ.event_color + " " + templ.tags + " " + scene.world_style_guide);
    entry.embedding.assign(embedded.begin(), embedded.end());

    scene.rag.narrative_cache.push_back(entry);
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
    scene.rag.narrative_cache.clear();
    for (const NarrativeTemplate& templ : kNarrativeFlavorTemplates) {
        AppendNarrativeTemplate(scene, templ);
    }
}

void TrimCache(Scene& scene) {
    if (scene.rag.spark_cache.size() > scene.rag.max_entries) {
        const std::size_t overshoot = scene.rag.spark_cache.size() - scene.rag.max_entries;
        scene.rag.spark_cache.erase(scene.rag.spark_cache.begin(), scene.rag.spark_cache.begin() + static_cast<std::ptrdiff_t>(overshoot));
    }

    if (scene.rag.narrative_cache.size() > scene.rag.max_entries) {
        const std::size_t narrative_overshoot = scene.rag.narrative_cache.size() - scene.rag.max_entries;
        scene.rag.narrative_cache.erase(
            scene.rag.narrative_cache.begin(),
            scene.rag.narrative_cache.begin() + static_cast<std::ptrdiff_t>(narrative_overshoot));
    }
}

std::string BuildNarrativeQueryText(const Scene& scene, const std::string& checkpoint, const Entity* focus_npc) {
    std::ostringstream query;
    query << "checkpoint " << checkpoint
          << " weather " << scene.weather.current_weather
          << " tone " << scene.weather.dialog_tone
          << " style " << scene.world_style_guide
          << " morale " << static_cast<int>(std::round(scene.settlement.morale))
          << " day " << scene.day_count;

    const std::size_t action_count = scene.recent_actions.size();
    const std::size_t start = action_count > 7U ? action_count - 7U : 0U;
    for (std::size_t i = start; i < action_count; ++i) {
        query << " change_log " << scene.recent_actions[i];
    }

    if (focus_npc != nullptr) {
        query << " npc " << focus_npc->id
              << " activity " << focus_npc->schedule.current_activity
              << " scripted_state " << focus_npc->scripted_behavior.current_state;
        auto relation_it = scene.relationships.find(focus_npc->id);
        if (relation_it != scene.relationships.end()) {
            query << " trust " << static_cast<int>(std::round(relation_it->second.trust))
                  << " respect " << static_cast<int>(std::round(relation_it->second.respect))
                  << " grudge " << static_cast<int>(std::round(relation_it->second.grudge));
        }
    }
    if (!scene.compressed_event_log.empty()) {
        const CompressedLegacyEvent& latest_legacy = scene.compressed_event_log.back();
        query << " legacy generation=" << latest_legacy.generation
              << " event=" << latest_legacy.event_type
              << " note=" << latest_legacy.summary;
    }
    return ToLower(query.str());
}

bool ContainsLegacyTrigger(const std::string& action_lower) {
    return action_lower.find("retirement") != std::string::npos ||
        action_lower.find("inheritance") != std::string::npos ||
        action_lower.find("legacy trigger") != std::string::npos ||
        action_lower.find("major legacy") != std::string::npos;
}

void TryCreateLegacySummaryFromRecentActions(Scene& scene) {
    if (scene.recent_actions.empty()) {
        return;
    }

    const std::string latest = ToLower(scene.recent_actions.back());
    if (!ContainsLegacyTrigger(latest)) {
        return;
    }

    if (!scene.compressed_event_log.empty() && scene.compressed_event_log.back().summary == scene.recent_actions.back()) {
        return;
    }

    const std::string event_type = latest.find("retirement") != std::string::npos
        ? "retirement"
        : (latest.find("inheritance") != std::string::npos ? "inheritance" : "major_legacy_trigger");
    std::hash<std::string> hasher{};
    const std::uint32_t seed = static_cast<std::uint32_t>(hasher(scene.recent_actions.back() + scene.legacy_summary_seed));
    RAGSystem::RecordLegacyEvent(scene, event_type, scene.recent_actions.back(), seed);
}

std::optional<RAGLegacyRecall> ResolveLegacyRecall(Scene& scene, const std::string& event_hint, bool count_metrics) {
    if (!scene.rag.enabled || scene.compressed_event_log.empty()) {
        if (count_metrics) {
            ++scene.rag.legacy_misses;
            scene.rag.last_legacy_source = "miss";
            scene.rag.last_legacy_similarity = -1.0F;
        }
        return std::nullopt;
    }

    const std::array<float, kEmbeddingDim> query = EmbedText(event_hint);
    float best_score = -1.0F;
    const CompressedLegacyEvent* best_entry = nullptr;
    for (const CompressedLegacyEvent& entry : scene.compressed_event_log) {
        const float score = CosineSimilarity(entry.embedding, query);
        if (score > best_score) {
            best_score = score;
            best_entry = &entry;
        }
    }

    if (best_entry == nullptr || best_score < scene.rag.similarity_threshold) {
        if (count_metrics) {
            ++scene.rag.legacy_misses;
            scene.rag.last_legacy_source = "miss";
            scene.rag.last_legacy_similarity = best_score;
        }
        return std::nullopt;
    }

    RAGLegacyRecall recall{};
    recall.summary = best_entry->summary;
    recall.event_type = best_entry->event_type;
    recall.generation = best_entry->generation;
    recall.similarity = best_score;
    recall.source = "legacy_rag";
    if (count_metrics) {
        ++scene.rag.legacy_hits;
        scene.rag.last_legacy_source = recall.source;
        scene.rag.last_legacy_similarity = best_score;
    }
    return recall;
}

RAGNarrativeFlavor FlavorFromEntry(const RAGCacheEntry& entry, float score, const std::string& source) {
    RAGNarrativeFlavor flavor{};
    flavor.checkpoint = entry.checkpoint;
    flavor.dialog_tone = "neutral";
    flavor.msq_branch = "default";
    flavor.event_color = "grounded";
    flavor.similarity = score;
    flavor.source = source;

    for (const NarrativeTemplate& templ : kNarrativeFlavorTemplates) {
        if (entry.checkpoint == templ.checkpoint && entry.tags.find(templ.tags) != std::string::npos) {
            flavor.dialog_tone = templ.dialog_tone;
            flavor.msq_branch = templ.msq_branch;
            flavor.event_color = templ.event_color;
            break;
        }
    }
    return flavor;
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
    const std::size_t max_legacy_entries = performance_mode ? kPerformanceLegacyMaxEntries : kDefaultLegacyMaxEntries;
    if (scene.compressed_event_log.size() > max_legacy_entries) {
        scene.compressed_event_log.erase(
            scene.compressed_event_log.begin(),
            scene.compressed_event_log.begin() +
                static_cast<std::ptrdiff_t>(scene.compressed_event_log.size() - max_legacy_entries));
    }
    if (scene.current_generation == 0U) {
        scene.current_generation = 1U;
    }
    if (scene.legacy_summary_seed.empty()) {
        scene.legacy_summary_seed = scene.world_style_guide + "_gen_" + std::to_string(scene.current_generation);
    }

    RebuildCacheIfNeeded(scene);
    TrimCache(scene);
    TryCreateLegacySummaryFromRecentActions(scene);

    const std::size_t total_entries = scene.rag.spark_cache.size() + scene.rag.narrative_cache.size();
    const float bytes = static_cast<float>(total_entries) * static_cast<float>(kEmbeddingDim * sizeof(float) + 256U);
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

std::optional<RAGNarrativeFlavor> RAGSystem::RetrieveNarrativeFlavor(
    Scene& scene,
    const std::string& checkpoint,
    const Entity* focus_npc) {
    EnsureDefaults(scene);
    if (!scene.rag.enabled || scene.rag.narrative_cache.empty()) {
        ++scene.rag.narrative_misses;
        scene.rag.last_narrative_source = "miss";
        return std::nullopt;
    }

    std::string query_text = BuildNarrativeQueryText(scene, checkpoint, focus_npc);
    if (const std::optional<RAGLegacyRecall> legacy = ResolveLegacyRecall(scene, query_text, false); legacy.has_value()) {
        query_text += " legacy_recall " + legacy->summary;
    }
    const std::array<float, kEmbeddingDim> query = EmbedText(query_text);
    float best_score = -1.0F;
    const RAGCacheEntry* best_entry = nullptr;
    for (const RAGCacheEntry& entry : scene.rag.narrative_cache) {
        if (entry.category != "narrative") {
            continue;
        }
        if (entry.checkpoint != checkpoint && entry.checkpoint != "generic") {
            continue;
        }
        const float score = CosineSimilarity(entry.embedding, query);
        if (score > best_score) {
            best_score = score;
            best_entry = &entry;
        }
    }

    if (best_entry == nullptr || best_score < scene.rag.similarity_threshold) {
        ++scene.rag.narrative_misses;
        scene.rag.last_narrative_source = "miss";
        scene.rag.last_narrative_checkpoint = checkpoint;
        scene.rag.last_narrative_similarity = best_score;
        return std::nullopt;
    }

    ++scene.rag.narrative_hits;
    scene.rag.last_narrative_source = "rag";
    scene.rag.last_narrative_checkpoint = checkpoint;
    scene.rag.last_narrative_similarity = best_score;
    const RAGNarrativeFlavor flavor = FlavorFromEntry(*best_entry, best_score, "rag");
    scene.rag.last_narrative_dialog_tone = flavor.dialog_tone;
    scene.rag.last_narrative_msq_branch = flavor.msq_branch;
    scene.rag.last_narrative_event_color = flavor.event_color;
    return flavor;
}

bool RAGSystem::EvaluateNarrativeCheckpoint(Scene& scene, const std::string& checkpoint) {
    EnsureDefaults(scene);
    const bool performance_mode = scene.optimization_overrides.lightweight_mode == "performance";
    if (performance_mode && (scene.rag.narrative_retrieve_tick++ % kPerformanceNarrativeStride) != 0U) {
        scene.rag.last_narrative_source = "throttled";
        scene.rag.last_narrative_checkpoint = checkpoint;
        return false;
    }

    const Entity* focus_npc = nullptr;
    if (scene.active_dialog_npc_id != 0U) {
        for (const Entity& entity : scene.entities) {
            if (entity.id == scene.active_dialog_npc_id) {
                focus_npc = &entity;
                break;
            }
        }
    }

    std::optional<RAGNarrativeFlavor> flavor = RetrieveNarrativeFlavor(scene, checkpoint, focus_npc);
    if (!flavor.has_value()) {
        scene.rag.last_narrative_source = "scripted";
        scene.rag.last_narrative_checkpoint = checkpoint;
        scene.rag.last_narrative_dialog_tone = "neutral";
        scene.rag.last_narrative_msq_branch = "msq_scripted_variation";
        scene.rag.last_narrative_event_color = "grounded";
        if (!performance_mode && scene.rag.live_fallback_enabled && scene.free_will.llm_enabled && !scene.free_will.model_path.empty()) {
            scene.rag.last_narrative_source = "llm";
            scene.rag.last_narrative_msq_branch = "msq_micro_llm_variation";
            ++scene.rag.narrative_live_fallback_calls;
        }
        return false;
    }

    return true;
}

void RAGSystem::RecordLegacyEvent(Scene& scene, const std::string& event_type, const std::string& summary, std::uint32_t seed) {
    if (summary.empty()) {
        return;
    }

    CompressedLegacyEvent event{};
    event.event_type = event_type.empty() ? "major_event" : ToLower(event_type);
    event.summary = summary;
    event.tags = "legacy generation memory";
    event.generation = std::max(1U, scene.current_generation);
    if (seed == 0U) {
        std::hash<std::string> hasher{};
        seed = static_cast<std::uint32_t>(hasher(summary + scene.legacy_summary_seed + event.event_type));
    }
    event.seed = seed;
    const std::array<float, kEmbeddingDim> embedded = EmbedText(
        event.event_type + " " + summary + " generation_" + std::to_string(event.generation) + " seed_" + std::to_string(seed));
    event.embedding.assign(embedded.begin(), embedded.end());
    scene.compressed_event_log.push_back(std::move(event));
    if (scene.compressed_event_log.size() > SceneLimits::kLegacyEventLogCap) {
        scene.compressed_event_log.erase(scene.compressed_event_log.begin());
    }
}

std::optional<RAGLegacyRecall> RAGSystem::RetrieveLegacyRecall(Scene& scene, const std::string& event_hint) {
    EnsureDefaults(scene);
    const bool performance_mode = scene.optimization_overrides.lightweight_mode == "performance";
    if (performance_mode && (scene.rag.legacy_retrieve_tick++ % kPerformanceNarrativeStride) != 0U) {
        scene.rag.last_legacy_source = "throttled";
        scene.rag.last_legacy_similarity = -1.0F;
        return std::nullopt;
    }
    return ResolveLegacyRecall(scene, event_hint, true);
}

std::string RAGSystem::BuildDebugSummary(Scene& scene, std::uint64_t npc_id) {
    EnsureDefaults(scene);
    std::ostringstream out;
    out << "RAG npc=" << npc_id
        << " entries=" << scene.rag.spark_cache.size()
        << " legacy_entries=" << scene.compressed_event_log.size()
        << " size_mb=" << scene.rag.cache_size_mb
        << " hit=" << scene.rag.cache_hits
        << " miss=" << scene.rag.cache_misses
        << " fallback_calls=" << scene.rag.live_fallback_calls
        << " narrative_hit=" << scene.rag.narrative_hits
        << " narrative_miss=" << scene.rag.narrative_misses
        << " narrative_fallback_calls=" << scene.rag.narrative_live_fallback_calls
        << " narrative_source=" << scene.rag.last_narrative_source
        << " legacy_source=" << scene.rag.last_legacy_source
        << " legacy_hit=" << scene.rag.legacy_hits
        << " legacy_miss=" << scene.rag.legacy_misses
        << " narrative_checkpoint=" << scene.rag.last_narrative_checkpoint
        << " last_source=" << scene.rag.last_source
        << " last_similarity=" << scene.rag.last_similarity;
    return out.str();
}
