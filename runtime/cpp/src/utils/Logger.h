#pragma once

#include <chrono>
#include <cstdlib>
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
        std::cout << line << '\n';
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
};

#define GF_LOG_INFO(message) Logger::Write("INFO", (message))
#define GF_LOG_WARN(message) Logger::Write("WARN", (message))
#define GF_LOG_ERROR(message) Logger::Write("ERROR", (message))
#define VK_CHECK(result)                                                                                               \
    do {                                                                                                               \
        const VkResult vk_check_result__ = (result);                                                                  \
        if (vk_check_result__ != VK_SUCCESS) {                                                                         \
            GF_LOG_ERROR(std::string("Vulkan failure: ") + #result + " -> code " + std::to_string(vk_check_result__)); \
            std::abort();                                                                                              \
        }                                                                                                              \
    } while (false)
