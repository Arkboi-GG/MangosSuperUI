# QuaternionData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuaternionData

**Purpose & Responsibilities**

`QuaternionData` is a lightweight, value-type struct defined in `GameObjectDefines.h` that represents a 3D rotation using quaternion components ($x, y, z, w$). It serves as the fundamental data container for orientation within the `wowvmangos` engine, specifically for `GameObject` instances.

Its primary responsibilities are:
1.  **Storage:** Holding the four floating-point components of a unit quaternion.
2.  **Initialization:** Providing constructors for identity rotations (default) and explicit component assignment.
3.  **Conversion Interface:** Declaring methods to convert between quaternions and Euler angles (ZYX order), facilitating interoperability with systems that define rotations via pitch, yaw, and roll (such as database storage or client protocols).

The struct itself contains no complex logic; its computational methods (`isUnit`, `toEulerAnglesZYX`, `fromEulerAnglesZYX`) are declared here but implemented elsewhere. The struct is tightly coupled with `GameObjectData`, which stores four `float` fields (`rotation0` through `rotation3`) that correspond directly to the $x, y, z, w$ components of a `QuaternionData` instance.

## Member-by-Member Behavior

### Constructors

#### **QuaternionData** (Default Constructor)
*   **Kind:** Constructor
*   **Behavior:** Initializes the quaternion to the **identity rotation**.
    *   Sets $x = 0.0f$, $y = 0.0f$, $z = 0.0f$.
    *   Sets $w = 1.0f$.
*   **Context:** This represents "no rotation" or a neutral orientation. It is used when a `QuaternionData` object is created without specific orientation data, ensuring a valid, normalized starting state.

#### **QuaternionData#2** (Parameterized Constructor)
*   **Kind:** Constructor
*   **Signature:** `QuaternionData(float X, float Y, float Z, float W)`
*   **Behavior:** Initializes the quaternion components directly from the provided arguments.
    *   Assigns $x = X$, $y = Y$, $z = Z$, $w = W$.
*   **Context:** This constructor allows for the direct construction of a quaternion from raw floating-point values. It is notably called by:
    *   `GameObject::fromEulerAnglesZYX`: To construct a quaternion after converting Euler angles.
    *   `GameObject::GetLocalRotation`: To package the current rotation components into a `QuaternionData` object for return.

### Declared Methods (Implementation Elsewhere)

While declared in this unit, the following methods are not implemented in `GameObjectDefines.h`. Their signatures define the interface for quaternion manipulation:

*   **`bool isUnit() const`**: Checks if the quaternion is normalized (i.e., $x^2 + y^2 + z^2 + w^2 \approx 1$). This is critical for ensuring valid rotations before performing calculations.
*   **`void toEulerAnglesZYX(float& Z, float& Y, float& X) const`**: Converts the internal quaternion representation into Euler angles in ZYX order (Yaw, Pitch, Roll). This is likely used for debugging, logging, or interfacing with systems that expect Euler angles.
*   **`static QuaternionData fromEulerAnglesZYX(float Z, float Y, float X)`**: A static factory method that creates a `QuaternionData` instance from ZYX Euler angles. This is the inverse of `toEulerAnglesZYX`.

## Cross-Unit Boundaries

`QuaternionData` acts as a passive data holder. Its interactions with other units are primarily through construction and data retrieval.

| Direction | Other Unit | Interaction Details |
| :--- | :--- | :--- |
| **Called By** | `GameObject` | `GameObject::fromEulerAnglesZYX` calls the parameterized constructor (`QuaternionData#2`) to create a quaternion from calculated Euler angles. |
| **Called By** | `GameObject` | `GameObject::GetLocalRotation` calls the parameterized constructor (`QuaternionData#2`) to wrap the current rotation floats into a `QuaternionData` object for external consumption. |

*Note: The MAP indicates no outgoing calls from `QuaternionData` members to other units. The struct relies on standard library operations and its own declared methods.*

## Data Model

`QuaternionData` does not directly interact with database tables. However, it is the in-memory representation of rotation data stored in the `gameobject` table.

Based on the `GameObjectData` struct in the same file, the relevant database columns are:
*   `rotation0`: Corresponds to `QuaternionData::x`
*   `rotation1`: Corresponds to `QuaternionData::y`
*   `rotation2`: Corresponds to `QuaternionData::z`
*   `rotation3`: Corresponds to `QuaternionData::w`

These values are loaded from the `gameobject` table into `GameObjectData`, and subsequently interpreted as a `QuaternionData` instance for rendering and physics calculations.

## Notable Implementation Details

1.  **Identity Initialization:** The default constructor explicitly sets $w=1.0f$ and $x,y,z=0.0f$. This is mathematically significant because $(0,0,0,1)$ is the identity quaternion. Failure to initialize $w$ to 1 would result in a zero vector, which is not a valid rotation and would cause undefined behavior in subsequent quaternion multiplications or conversions.
2.  **ZYX Convention:** The conversion methods specify `ZYX` order. This implies a specific axis convention (likely Yaw around Z, Pitch around Y, Roll around X, or similar depending on the coordinate system). Maintainers must ensure that any code converting between Euler angles and quaternions respects this specific order to avoid gimbal lock issues or incorrect orientations.
3.  **Value Semantics:** As a simple struct with public members, `QuaternionData` is copied by value. This is efficient for small data structures but requires care when passing large numbers of rotations to avoid unnecessary copies. However, given its small size (4 floats), pass-by-value is generally acceptable and often preferred for clarity.
4.  **No Validation in Constructor:** The parameterized constructor does not validate whether the input components form a unit quaternion. It assumes the caller provides valid data. If invalid data is passed, `isUnit()` will return false, and subsequent operations may produce incorrect results. Validation is deferred to the `isUnit()` method or the caller.

## Member Reference

**QuaternionData**
The default constructor initializes the quaternion to the identity rotation ($x=0, y=0, z=0, w=1$). This ensures a valid, neutral orientation by default.

**QuaternionData#2**
The parameterized constructor initializes the quaternion components ($x, y, z, w$) from the provided float arguments. It is used by `GameObject::fromEulerAnglesZYX` and `GameObject::GetLocalRotation` to create quaternion instances from raw data.

---

<!-- machine-true, projected from graph.json -->

## Map — QuaternionData

*Source:* GameObjectDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuaternionData | ctor | — | — | — |
| QuaternionData#2 | ctor | — | GameObject/fromEulerAnglesZYX, GameObject/GetLocalRotation | — |
