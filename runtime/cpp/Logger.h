#pragma once

#include <cstdlib>
#include <chrono>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <mutex>
#include <sstream>
#include <string>

#include <vulkan/vulkan.h>

class Logger {
public:
    static void Init(const std::string& file_path = "forgeengine.log") {
        std::lock_guard<std::mutex> lock(Mutex());
        File().open(file_path, std::ios::out | std::ios::trunc);
    }

    static void Shutdown() {
        std::lock_guard<std::mutex> lock(Mutex());
        if (File().is_open()) {
            File().flush();
            File().close();
        }
    }

    static void Write(const char* level, const std::string& message) {
        std::lock_guard<std::mutex> lock(Mutex());
        const std::string line = TimeStamp() + " [" + level + "] " + message;
        std::cout << ColorForLevel(level) << line << "\033[0m" << '\n';
        if (File().is_open()) {
            File() << line << '\n';
            File().flush();
        }
    }

private:
    static std::string TimeStamp() {
        const auto now = std::chrono::system_clock::now();
        const auto time_t = std::chrono::system_clock::to_time_t(now);
        std::tm local_tm{};
#ifdef _WIN32
        localtime_s(&local_tm, &time_t);
#else
        localtime_r(&time_t, &local_tm);
#endif

        std::ostringstream stream;
        stream << std::put_time(&local_tm, "%Y-%m-%d %H:%M:%S");
        return stream.str();
    }

    static std::ofstream& File() {
        static std::ofstream file;
        return file;
    }

    static std::mutex& Mutex() {
        static std::mutex mutex;
        return mutex;
    }

    static const char* ColorForLevel(const char* level) {
        if (level == nullptr) {
            return "\033[0m";
        }
        if (std::string(level) == "ERROR") {
            return "\033[1;31m";
        }
        if (std::string(level) == "WARN") {
            return "\033[1;33m";
        }
        if (std::string(level) == "INFO") {
            return "\033[1;32m";
        }
        return "\033[0m";
    }
};

#define GF_LOG_INFO(message) Logger::Write("INFO", (message))
#define GF_LOG_WARN(message) Logger::Write("WARN", (message))
#define GF_LOG_ERROR(message) Logger::Write("ERROR", (message))
#define VK_CHECK(expression)                                                                                              \
    do {                                                                                                                  \
        const VkResult vk_check_result = (expression);                                                                    \
        if (vk_check_result != VK_SUCCESS) {                                                                              \
            GF_LOG_ERROR("Vulkan call failed with VkResult " + std::to_string(static_cast<int>(vk_check_result)));      \
            std::abort();                                                                                                 \
        }                                                                                                                 \
    } while (false)
