﻿// Kerbal Attachment System
// Mod's author: KospY (http://forum.kerbalspaceprogram.com/index.php?/profile/33868-kospy/)
// Module author: igor.zavoychinskiy@gmail.com
// License: https://github.com/KospY/KAS/blob/master/LICENSE.md
using System;
using System.Linq;
using System.Collections;
using System.Text;
using UnityEngine;
using KASAPIv1;
using KSPDev.KSPInterfaces;
using KSPDev.GUIUtils;

namespace KAS {

//FIXME docs
public sealed class KASModuleTwoEndsSphereJoint : AbstractJointModule, IModuleInfo, ILinkJoint,
                                                  IKSPDevModuleInfo {
  #region Helper class to detect joint breakage
  /// <summary>Helper class to detect sphere joint ends breakage.</summary>
  /// <remarks>When joint breaks the source part is get decoupled from the parent.</remarks>
  class BrokenJointListener : MonoBehaviour {
    /// <summary>Part to decouple on joint break.</summary>
    public Part host;

    /// <summary>Triggers when joint break force if exceeded.</summary>
    /// <remarks>Overridden from <see cref="MonoBehaviour"/>.</remarks>
    /// <param name="breakForce">Actual force that broke the joint.</param>
    void OnJointBreak(float breakForce) {
      if (host.parent != null) {
        Debug.LogFormat("Joint {0} broken with force: {1}", this.gameObject.name, breakForce);
        host.OnPartJointBreak(breakForce);
      }
    }
  }
  #endregion

  // These fields must not be accessed outside of the module. They are declared public only
  // because KSP won't work otherwise. Ancenstors and external callers must access values via
  // interface properties. If property is not there then it means it's *intentionally* restricted
  // for the non-internal consumers.
  #region Part's config fields
  #endregion

  /// <summary>Source sphere joint.</summary>
  /// <remarks>It doesn't allow linear movements but does allow rotation around any axis. Rotation
  /// can be limited via a configuration parameter <see cref="sourceLinkAngleLimit"/>. The joint is
  /// unbreakable by the linear force but can be broken by torque.</remarks>
  ConfigurableJoint srcJoint;
  /// <summary>Target sphere joint.</summary>
  /// <remarks>It doesn't allow linear movements but does allow rotation around any axis. Rotation
  /// can be limited via a configuration parameter <see cref="targetLinkAngleLimit"/>. The joint is
  /// unbreakable by the linear force but can be broken by torque.</remarks>
  ConfigurableJoint trgJoint;
  //FIXME
  ConfigurableJoint strutJoint;
  ILinkSource linkSource;
  ILinkTarget linkTarget;
  float originalLength;

  #region ILinkJoint implementation
  /// <inheritdoc/>
  public override void SetupJoint(ILinkSource source, ILinkTarget target) {
    //FIXME: check for limits
    CleanupJoint();
    linkSource = source;
    linkTarget = target;
    srcJoint = CreateKinematicJointEnd(source.attachNode, "KASJointSrc");
    trgJoint = CreateKinematicJointEnd(target.attachNode, "KASJointTrg");
    StartCoroutine(WaitAndConnectJointEnds());
  }

  /// <inheritdoc/>
  public override void CleanupJoint() {
    UnityEngine.Object.Destroy(srcJoint);
    srcJoint = null;
    UnityEngine.Object.Destroy(trgJoint);
    trgJoint = null;
    strutJoint = null;
    linkSource = null;
    linkTarget = null;
  }
  #endregion

  /// <summary>Destroys child objects when joint is destroyed.</summary>
  /// <para>Overridden from <see cref="MonoBehaviour"/>.</para>
  void OnDestroy() {
    CleanupJoint();
  }

  #region Private utility methods
  /// <summary>Creates a game object joined with the attach node.</summary>
  /// <remarks>The created object has kinematic rigidbody and its transform is parented to the
  /// node's owner. This opbject must be promoted to physical once PhysX received the configuration.
  /// </remarks>
  /// <param name="an">Node to attach a new spheric joint to.</param>
  /// <param name="objName">Name of the game object for the new joint.</param>
  /// <returns>Object that owns the joint.</returns>
  ConfigurableJoint CreateKinematicJointEnd(AttachNode an, string objName) {
    //FIXME
    Debug.LogWarningFormat("Make joint for: {0} (id={1}), {2}",
                           an.owner.name, an.owner.flightID, an.id);
    var objJoint = new GameObject(objName);
    objJoint.AddComponent<BrokenJointListener>().host = part;
    var jointRb = objJoint.AddComponent<Rigidbody>();
    jointRb.isKinematic = true;
    objJoint.transform.parent = an.owner.transform;
    objJoint.transform.localPosition = an.position;
    objJoint.transform.localRotation = Quaternion.LookRotation(an.orientation, an.secondaryAxis);
    var joint = objJoint.AddComponent<ConfigurableJoint>();
    KASAPI.JointUtils.ResetJoint(joint);
    KASAPI.JointUtils.SetupSphericalJoint(joint, angleLimit: sourceLinkAngleLimit);
    joint.enablePreprocessing = true;
    joint.connectedBody = an.owner.rb;
    return joint;
  }

  /// <summary>Waits for the next fixed update and connects the joint ends.</summary>
  /// <remarks>Waiting is required to have the spherical joints info sent to the PhysX engine. This
  /// must happen when both ends are at their "kinematic" positions to let engine capture the
  /// initial angles. Once it's done it's time to rotate the ends so what they look at each other,
  /// and attach them with a fixed or prismatic joint.</remarks>
  /// <returns><c>null</c> when it's time to terminate.</returns>
  IEnumerator WaitAndConnectJointEnds() {
    // Allow fixed update to have the joint info sent to PhysX. This will capture initial sphere
    // joints rotations.
    //FIXME
    Debug.LogWarning("Wait for sphere joints to populate");
    yield return new WaitForFixedUpdate();

    // Now rotate both sphere joints towards each other, and connect them with a prismatic joint.
    var srcRb = srcJoint.gameObject.GetComponent<Rigidbody>();
    var trgRb = trgJoint.gameObject.GetComponent<Rigidbody>();
    srcJoint.transform.LookAt(trgJoint.transform);
    trgJoint.transform.LookAt(srcJoint.transform);
    strutJoint = srcJoint.gameObject.AddComponent<ConfigurableJoint>();
    KASAPI.JointUtils.ResetJoint(strutJoint);
    KASAPI.JointUtils.SetupPrismaticJoint(strutJoint, springForce: Mathf.Infinity);
    originalLength = Vector3.Distance(srcJoint.transform.position, trgJoint.transform.position);
    strutJoint.enablePreprocessing = true;
    strutJoint.connectedBody = trgRb;
    
    // Allow another fixed update to happen to remember strut positions.
    //FIXME
    Debug.LogWarning("Wait for strut joints to populate");
    yield return new WaitForFixedUpdate();

    // Setup breaking forces:
    // - Sphere joints should only check for the breaking forces, so set torque limit only.
    // - Strut joint should only check for the stretch/compress forces, so set breaking force only.
    //   Note, that in Unity this kind of force is indistinguishable from the normal acceleration.
    //   I.e. the joint will break even if there is no actualy stretching, but the part's ends got
    //   an impulse.
    var breakTorque =
        Mathf.Approximately(linkBreakTorque, 0) ? Mathf.Infinity : linkBreakTorque;
    strutJoint.breakForce =
        Mathf.Approximately(linkBreakForce, 0) ? Mathf.Infinity : linkBreakForce;
    strutJoint.breakTorque = Mathf.Infinity;
    srcJoint.gameObject.AddComponent<BrokenJointListener>().host = part;
    srcJoint.breakForce = Mathf.Infinity;
    srcJoint.breakTorque = breakTorque;
    trgJoint.gameObject.AddComponent<BrokenJointListener>().host = part;
    trgJoint.breakForce = Mathf.Infinity;
    trgJoint.breakTorque = breakTorque;

    // Re-align the ends in case of parent objects have moved in the last physics frame. Here
    // physics effects may show up.
    srcJoint.transform.LookAt(trgJoint.transform);
    trgJoint.transform.LookAt(srcJoint.transform);

    // Promote source and target rigid bodies to physical. From now on they live in a system of
    // three joints.
    srcRb.isKinematic = false;
    srcJoint.transform.parent = null;
    trgRb.isKinematic = false;
    trgJoint.transform.parent = null;
    
    //FIXME
    Debug.LogWarning("Joint promoted to physics");
    DestroyStockJoint();
  }

  /// <summary>Destroys the joint created by KSP to tie the parts together.</summary>
  /// <remarks>To eliminate the side effects drop the joint only when the alternative is ready for
  /// use.</remarks>
  void DestroyStockJoint() {
    if (linkSource.part.attachJoint != null) {
      //FIXME
      Debug.LogWarning("drop src-to-trg joint");
      linkSource.part.attachJoint.DestroyJoint();
      linkSource.part.attachJoint = null;
    }
  }
  #endregion
}

}  // namespace