const bcrypt = require("bcrypt");
const userRepository = require("../repositories/userRepository");

async function registerUser({
    username,
    email,
    password,
    role = "User"
}) {
    const existingUsername =
        await userRepository.getUserByUsername(username);

    if (existingUsername) {
        throw new Error("Username already exists");
    }

    const existingEmail =
        await userRepository.getUserByEmail(email);

    if (existingEmail) {
        throw new Error("Email already exists");
    }

    const passwordHash = await bcrypt.hash(password, 12);

    return userRepository.createUser({
        username,
        email,
        passwordHash,
        role
    });
}

async function authenticateUser(username, password) {
    const user =
        await userRepository.getUserByUsername(username);

    if (!user) {
        return null;
    }

    if (!user.is_active) {
        return null;
    }

    const passwordMatches =
        await bcrypt.compare(password, user.password_hash);

    if (!passwordMatches) {
        return null;
    }

    return {
        id: user.id,
        username: user.username,
        email: user.email,
        role: user.role,
        isActive: user.is_active,
        createdAt: user.created_at
    };
}

module.exports = {
    registerUser,
    authenticateUser
};