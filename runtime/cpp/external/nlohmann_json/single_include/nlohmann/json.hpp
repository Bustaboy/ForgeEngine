#pragma once

#include <cctype>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <initializer_list>
#include <istream>
#include <map>
#include <ostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <utility>
#include <variant>
#include <vector>

namespace nlohmann {

class json {
public:
    using array_t = std::vector<json>;
    using object_t = std::map<std::string, json>;

    enum class Type {
        Null,
        Boolean,
        Number,
        String,
        Array,
        Object,
    };

    json() : type_(Type::Null), value_(nullptr) {}
    json(std::nullptr_t) : type_(Type::Null), value_(nullptr) {}
    json(bool value) : type_(Type::Boolean), value_(value) {}
    json(int value) : type_(Type::Number), value_(static_cast<double>(value)) {}
    json(unsigned int value) : type_(Type::Number), value_(static_cast<double>(value)) {}
    json(long value) : type_(Type::Number), value_(static_cast<double>(value)) {}
    json(unsigned long value) : type_(Type::Number), value_(static_cast<double>(value)) {}
    json(long long value) : type_(Type::Number), value_(static_cast<double>(value)) {}
    json(unsigned long long value) : type_(Type::Number), value_(static_cast<double>(value)) {}
    json(float value) : type_(Type::Number), value_(static_cast<double>(value)) {}
    json(double value) : type_(Type::Number), value_(value) {}
    json(const char* value) : type_(Type::String), value_(std::string(value)) {}
    json(const std::string& value) : type_(Type::String), value_(value) {}
    json(std::string&& value) : type_(Type::String), value_(std::move(value)) {}

    json(std::initializer_list<std::pair<const std::string, json>> init) : type_(Type::Object), value_(object_t{}) {
        auto& object = std::get<object_t>(value_);
        for (const auto& item : init) {
            object[item.first] = item.second;
        }
    }

    static json array() {
        json value;
        value.type_ = Type::Array;
        value.value_ = array_t{};
        return value;
    }

    bool is_object() const { return type_ == Type::Object; }
    bool is_array() const { return type_ == Type::Array; }

    bool contains(const std::string& key) const {
        if (!is_object()) {
            return false;
        }
        const auto& object = std::get<object_t>(value_);
        return object.find(key) != object.end();
    }

    std::size_t size() const {
        if (is_array()) {
            return std::get<array_t>(value_).size();
        }
        if (is_object()) {
            return std::get<object_t>(value_).size();
        }
        return 0;
    }

    void reserve(std::size_t n) {
        if (!is_array()) {
            type_ = Type::Array;
            value_ = array_t{};
        }
        std::get<array_t>(value_).reserve(n);
    }

    void push_back(const json& item) {
        if (!is_array()) {
            type_ = Type::Array;
            value_ = array_t{};
        }
        std::get<array_t>(value_).push_back(item);
    }

    json& operator[](const std::string& key) {
        if (!is_object()) {
            type_ = Type::Object;
            value_ = object_t{};
        }
        return std::get<object_t>(value_)[key];
    }

    const json& operator[](const std::string& key) const {
        if (!is_object()) {
            throw std::out_of_range("json value is not an object");
        }
        const auto& object = std::get<object_t>(value_);
        const auto it = object.find(key);
        if (it == object.end()) {
            throw std::out_of_range("json key not found");
        }
        return it->second;
    }

    array_t::iterator begin() {
        if (!is_array()) {
            throw std::runtime_error("json value is not an array");
        }
        return std::get<array_t>(value_).begin();
    }

    array_t::iterator end() {
        if (!is_array()) {
            throw std::runtime_error("json value is not an array");
        }
        return std::get<array_t>(value_).end();
    }

    array_t::const_iterator begin() const {
        if (!is_array()) {
            throw std::runtime_error("json value is not an array");
        }
        return std::get<array_t>(value_).begin();
    }

    array_t::const_iterator end() const {
        if (!is_array()) {
            throw std::runtime_error("json value is not an array");
        }
        return std::get<array_t>(value_).end();
    }

    template <typename T>
    T value(const std::string& key, const T& default_value) const {
        if (!is_object()) {
            return default_value;
        }
        const auto& object = std::get<object_t>(value_);
        const auto it = object.find(key);
        if (it == object.end()) {
            return default_value;
        }
        return it->second.get_or(default_value);
    }

    std::string dump(int indent = -1) const {
        std::string output;
        dump_to(output, indent, 0);
        return output;
    }

    friend std::istream& operator>>(std::istream& is, json& out) {
        const std::string content((std::istreambuf_iterator<char>(is)), std::istreambuf_iterator<char>());
        std::size_t pos = 0;
        skip_ws(content, pos);
        out = parse_value(content, pos);
        skip_ws(content, pos);
        if (pos != content.size()) {
            throw std::runtime_error("unexpected trailing characters in json");
        }
        return is;
    }

private:
    Type type_;
    std::variant<std::nullptr_t, bool, double, std::string, array_t, object_t> value_;

    template <typename T>
    T get_or(const T& fallback) const {
        if constexpr (std::is_same_v<T, std::string>) {
            if (type_ == Type::String) {
                return std::get<std::string>(value_);
            }
            return fallback;
        } else if constexpr (std::is_same_v<T, bool>) {
            if (type_ == Type::Boolean) {
                return std::get<bool>(value_);
            }
            return fallback;
        } else if constexpr (std::is_integral_v<T>) {
            if (type_ == Type::Number) {
                return static_cast<T>(std::get<double>(value_));
            }
            return fallback;
        } else if constexpr (std::is_floating_point_v<T>) {
            if (type_ == Type::Number) {
                return static_cast<T>(std::get<double>(value_));
            }
            return fallback;
        } else {
            return fallback;
        }
    }

    static void skip_ws(const std::string& s, std::size_t& pos) {
        while (pos < s.size() && std::isspace(static_cast<unsigned char>(s[pos])) != 0) {
            ++pos;
        }
    }

    static json parse_value(const std::string& s, std::size_t& pos) {
        skip_ws(s, pos);
        if (pos >= s.size()) {
            throw std::runtime_error("unexpected end of json");
        }

        const char c = s[pos];
        if (c == '{') {
            return parse_object(s, pos);
        }
        if (c == '[') {
            return parse_array(s, pos);
        }
        if (c == '"') {
            return json(parse_string(s, pos));
        }
        if (c == 't') {
            expect_literal(s, pos, "true");
            return json(true);
        }
        if (c == 'f') {
            expect_literal(s, pos, "false");
            return json(false);
        }
        if (c == 'n') {
            expect_literal(s, pos, "null");
            return json(nullptr);
        }
        return json(parse_number(s, pos));
    }

    static void expect_literal(const std::string& s, std::size_t& pos, const char* literal) {
        std::size_t i = 0;
        while (literal[i] != '\0') {
            if (pos + i >= s.size() || s[pos + i] != literal[i]) {
                throw std::runtime_error("invalid json literal");
            }
            ++i;
        }
        pos += i;
    }

    static std::string parse_string(const std::string& s, std::size_t& pos) {
        if (s[pos] != '"') {
            throw std::runtime_error("expected string");
        }
        ++pos;
        std::string result;
        while (pos < s.size()) {
            const char c = s[pos++];
            if (c == '"') {
                return result;
            }
            if (c == '\\') {
                if (pos >= s.size()) {
                    throw std::runtime_error("invalid string escape");
                }
                const char esc = s[pos++];
                switch (esc) {
                    case '"': result.push_back('"'); break;
                    case '\\': result.push_back('\\'); break;
                    case '/': result.push_back('/'); break;
                    case 'b': result.push_back('\b'); break;
                    case 'f': result.push_back('\f'); break;
                    case 'n': result.push_back('\n'); break;
                    case 'r': result.push_back('\r'); break;
                    case 't': result.push_back('\t'); break;
                    default: throw std::runtime_error("unsupported string escape");
                }
            } else {
                result.push_back(c);
            }
        }
        throw std::runtime_error("unterminated string");
    }

    static double parse_number(const std::string& s, std::size_t& pos) {
        const std::size_t start = pos;
        if (s[pos] == '-') {
            ++pos;
        }
        while (pos < s.size() && std::isdigit(static_cast<unsigned char>(s[pos])) != 0) {
            ++pos;
        }
        if (pos < s.size() && s[pos] == '.') {
            ++pos;
            while (pos < s.size() && std::isdigit(static_cast<unsigned char>(s[pos])) != 0) {
                ++pos;
            }
        }
        if (pos < s.size() && (s[pos] == 'e' || s[pos] == 'E')) {
            ++pos;
            if (pos < s.size() && (s[pos] == '+' || s[pos] == '-')) {
                ++pos;
            }
            while (pos < s.size() && std::isdigit(static_cast<unsigned char>(s[pos])) != 0) {
                ++pos;
            }
        }
        const std::string number = s.substr(start, pos - start);
        char* end_ptr = nullptr;
        const double value = std::strtod(number.c_str(), &end_ptr);
        if (end_ptr == number.c_str() || *end_ptr != '\0') {
            throw std::runtime_error("invalid json number");
        }
        return value;
    }

    static json parse_array(const std::string& s, std::size_t& pos) {
        if (s[pos] != '[') {
            throw std::runtime_error("expected array");
        }
        ++pos;
        json result = json::array();
        skip_ws(s, pos);
        if (pos < s.size() && s[pos] == ']') {
            ++pos;
            return result;
        }

        while (true) {
            result.push_back(parse_value(s, pos));
            skip_ws(s, pos);
            if (pos >= s.size()) {
                throw std::runtime_error("unterminated array");
            }
            if (s[pos] == ']') {
                ++pos;
                break;
            }
            if (s[pos] != ',') {
                throw std::runtime_error("expected comma in array");
            }
            ++pos;
        }

        return result;
    }

    static json parse_object(const std::string& s, std::size_t& pos) {
        if (s[pos] != '{') {
            throw std::runtime_error("expected object");
        }
        ++pos;
        json result({});
        result.type_ = Type::Object;
        result.value_ = object_t{};

        skip_ws(s, pos);
        if (pos < s.size() && s[pos] == '}') {
            ++pos;
            return result;
        }

        while (true) {
            skip_ws(s, pos);
            const std::string key = parse_string(s, pos);
            skip_ws(s, pos);
            if (pos >= s.size() || s[pos] != ':') {
                throw std::runtime_error("expected colon in object");
            }
            ++pos;
            result[key] = parse_value(s, pos);
            skip_ws(s, pos);
            if (pos >= s.size()) {
                throw std::runtime_error("unterminated object");
            }
            if (s[pos] == '}') {
                ++pos;
                break;
            }
            if (s[pos] != ',') {
                throw std::runtime_error("expected comma in object");
            }
            ++pos;
        }

        return result;
    }

    static std::string escape_string(const std::string& value) {
        std::string escaped;
        escaped.reserve(value.size());
        for (char c : value) {
            switch (c) {
                case '"': escaped += "\\\""; break;
                case '\\': escaped += "\\\\"; break;
                case '\b': escaped += "\\b"; break;
                case '\f': escaped += "\\f"; break;
                case '\n': escaped += "\\n"; break;
                case '\r': escaped += "\\r"; break;
                case '\t': escaped += "\\t"; break;
                default: escaped.push_back(c); break;
            }
        }
        return escaped;
    }

    void dump_to(std::string& out, int indent, int depth) const {
        switch (type_) {
            case Type::Null:
                out += "null";
                break;
            case Type::Boolean:
                out += std::get<bool>(value_) ? "true" : "false";
                break;
            case Type::Number: {
                std::ostringstream stream;
                stream.precision(15);
                stream << std::get<double>(value_);
                out += stream.str();
                break;
            }
            case Type::String:
                out += '"';
                out += escape_string(std::get<std::string>(value_));
                out += '"';
                break;
            case Type::Array: {
                const auto& array = std::get<array_t>(value_);
                out += '[';
                if (!array.empty()) {
                    bool first = true;
                    for (const auto& item : array) {
                        if (!first) {
                            out += ',';
                        }
                        if (indent >= 0) {
                            out += '\n';
                            out.append(static_cast<std::size_t>((depth + 1) * indent), ' ');
                        }
                        item.dump_to(out, indent, depth + 1);
                        first = false;
                    }
                    if (indent >= 0) {
                        out += '\n';
                        out.append(static_cast<std::size_t>(depth * indent), ' ');
                    }
                }
                out += ']';
                break;
            }
            case Type::Object: {
                const auto& object = std::get<object_t>(value_);
                out += '{';
                if (!object.empty()) {
                    bool first = true;
                    for (const auto& [key, value] : object) {
                        if (!first) {
                            out += ',';
                        }
                        if (indent >= 0) {
                            out += '\n';
                            out.append(static_cast<std::size_t>((depth + 1) * indent), ' ');
                        }
                        out += '"';
                        out += escape_string(key);
                        out += '"';
                        out += ':';
                        if (indent >= 0) {
                            out += ' ';
                        }
                        value.dump_to(out, indent, depth + 1);
                        first = false;
                    }
                    if (indent >= 0) {
                        out += '\n';
                        out.append(static_cast<std::size_t>(depth * indent), ' ');
                    }
                }
                out += '}';
                break;
            }
        }
    }
};

}  // namespace nlohmann
