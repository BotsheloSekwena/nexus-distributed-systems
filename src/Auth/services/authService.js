
const bcrypt = require("bcrypt");
const crypto = require("crypto");
const userRepository = require("../repositories/userRepository");

// Generate a random dummy hash for unknown and inactive accounts.
const dummyPasswordHashPromise = bcrypt.hash(
    crypto.randomBytes(32).toString("hex"),
    12
);

/**
 * Registers a new standard user.
 */
async function registerUser({ username, email, password }) {
    const normalizedUsername = username.trim();
    const normalizedEmail = email.trim().toLowerCase();

    const existingUsername =
        await userRepository.getUserByUsername(normalizedUsername);

    if (existingUsername) {
        throw new Error("Username already exists");
    }

    const existingEmail =
        await userRepository.getUserByEmail(normalizedEmail);

    if (existingEmail) {
        throw new Error("Email already exists");
    }

    const passwordHash = await bcrypt.hash(password, 12);

    return userRepository.createUser({
        username: normalizedUsername,
        email: normalizedEmail,
        passwordHash,
        role: "User"
    });
}

/**
 * Authenticates a user and returns their current token version.
 */
async function authenticateUser(username, password) {
    const normalizedUsername = username.trim();

    const user =
        await userRepository.getUserByUsername(normalizedUsername);

    if (!user || !user.is_active) {
        const dummyHash = await dummyPasswordHashPromise;
        await bcrypt.compare(password, dummyHash);
        return null;
    }

    const passwordMatches = await bcrypt.compare(
        password,
        user.password_hash
    );

    if (!passwordMatches) {
        return null;
    }

    return {
        id: user.id,
        username: user.username,
        email: user.email,
        role: user.role,
        isActive: user.is_active,
        tokenVersion: user.token_version,
        createdAt: user.created_at
    };
}

/**
 * Validates a JWT against the current database account state.
 */
async function validateToken({ userId, tokenVersion, role }) {
    return userRepository.isTokenValid(
        userId,
        tokenVersion,
        role
    );
}

module.exports = {
    registerUser,
    authenticateUser,
    validateToken
};