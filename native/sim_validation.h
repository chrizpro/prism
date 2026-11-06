#pragma once

#include <cmath>
#include <limits>
#include <stdexcept>

namespace sim_validation {
inline void require(bool valid, const char* message) {
    if (!valid) throw std::invalid_argument(message);
}

inline void count(int n, int stride) {
    require(n >= 0 && n <= std::numeric_limits<int>::max() / stride, "Invalid array count.");
}

template <class T> void array(const T* data, int n, int stride = 1) {
    count(n, stride);
    require(n == 0 || data, "A required input array is null.");
}

inline void indices(const int* data, int n, int limit, int stride = 1) {
    array(data, n, stride);
    for (int i = 0; i < n * stride; ++i)
        require(data[i] >= 0 && data[i] < limit, "Mesh index out of range.");
}

template <class T> void finite(const T* data, int n, int stride = 1) {
    array(data, n, stride);
    for (int i = 0; i < n * stride; ++i)
        require(std::isfinite(data[i]), "Input values must be finite.");
}

inline void material(const double* mat) {
    finite(mat, 3);
    require(mat[0] > 0 && mat[1] > -1 && mat[1] < 0.5 && mat[2] >= 0,
            "Invalid elastic modulus, Poisson ratio, or density.");
}
} // namespace sim_validation
