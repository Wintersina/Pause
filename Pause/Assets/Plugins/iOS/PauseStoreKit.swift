// StoreKit 2 bridge for AppStoreBilling.cs (the iOS counterpart of PlayBilling): products, purchases,
// Transaction.updates, current entitlements and Restore Purchases.
//
// Talks to C# through plain @_cdecl functions (DllImport "__Internal") and one callback registered at
// PauseStore_Init: callback(kind, payload), both UTF-8 C strings, always invoked on the main queue. Payloads are
// tab separated fields, one record per line; AppleStoreMessages.cs parses them. Kinds:
//   products      "<productId>\t<displayPrice>" per line (only ids the App Store returned)
//   tx            "<transactionId>\t<productId>\t<originalId>\t<purchased|revoked>"   (verified transactions only)
//   pending       "<productId>"          Ask to Buy / SCA: nothing is granted until a later `tx`
//   cancelled     "<productId>"
//   error         "<productId or ->\t<message>"
//   entitlements  "<productId>" per line: the non-consumables currently owned (may be empty)
//   restored      "ok" or "error\t<message>"  (after AppStore.sync(); `entitlements` is sent first)
//
// A transaction is only finished when C# calls PauseStore_Finish, after the game granted it. Unfinished
// transactions are redelivered through Transaction.updates / Transaction.unfinished at the next launch.
//
// Needs iOS 15 (async StoreKit 2). Built into UnityFramework; IOSCapabilitiesPostProcess links StoreKit.framework.

import Foundation
import StoreKit

public typealias PauseStoreCallback = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void

private var pauseStoreCallback: PauseStoreCallback?
private var pauseStoreProducts: [String: Any] = [:]       // Product, boxed so the file loads on older OS versions
private var pauseStorePending: [String: Any] = [:]        // Transaction by id, until finished
private var pauseStoreUpdates: Any?                       // Task<Void, Never>

private func pauseStoreSend(_ kind: String, _ payload: String) {
    DispatchQueue.main.async {
        guard let cb = pauseStoreCallback else { return }
        kind.withCString { k in payload.withCString { p in cb(k, p) } }
    }
}

@available(iOS 15.0, *)
private func pauseStoreHandle(_ result: VerificationResult<Transaction>) {
    switch result {
    case .verified(let tx):
        pauseStorePending[String(tx.id)] = tx
        let state = tx.revocationDate == nil ? "purchased" : "revoked"
        pauseStoreSend("tx", "\(tx.id)\t\(tx.productID)\t\(tx.originalID)\t\(state)")
    case .unverified(let tx, let error):
        // Never grant or finish what the signature check rejected.
        pauseStoreSend("error", "\(tx.productID)\tunverified: \(error.localizedDescription)")
    }
}

@available(iOS 15.0, *)
private func pauseStoreEntitlements() async {
    var owned: [String] = []
    for await result in Transaction.currentEntitlements {
        if case .verified(let tx) = result, tx.revocationDate == nil, tx.productType == .nonConsumable {
            owned.append(tx.productID)
        }
    }
    pauseStoreSend("entitlements", owned.joined(separator: "\n"))
}

@_cdecl("PauseStore_Init")
public func PauseStore_Init(_ callback: PauseStoreCallback) {
    pauseStoreCallback = callback
    guard #available(iOS 15.0, *) else { return }
    if pauseStoreUpdates != nil { return }
    // The updates listener has to run for the whole life of the app: Ask to Buy approvals, purchases made
    // outside the app, renewals and the redelivery of anything not finished all arrive here.
    pauseStoreUpdates = Task.detached {
        for await result in Transaction.updates { pauseStoreHandle(result) }
    }
    Task {
        for await result in Transaction.unfinished { pauseStoreHandle(result) }
        await pauseStoreEntitlements()
    }
}

@_cdecl("PauseStore_LoadProducts")
public func PauseStore_LoadProducts(_ idsCsv: UnsafePointer<CChar>) {
    let ids = String(cString: idsCsv).split(separator: ",").map { String($0) }
    guard #available(iOS 15.0, *) else { pauseStoreSend("products", ""); return }
    Task {
        do {
            let found = try await Product.products(for: ids)
            var lines: [String] = []
            for p in found {
                pauseStoreProducts[p.id] = p
                lines.append("\(p.id)\t\(p.displayPrice)")
            }
            pauseStoreSend("products", lines.joined(separator: "\n"))
        } catch {
            pauseStoreSend("error", "-\tproducts: \(error.localizedDescription)")
            pauseStoreSend("products", "")
        }
    }
}

@_cdecl("PauseStore_Buy")
public func PauseStore_Buy(_ productId: UnsafePointer<CChar>) {
    let id = String(cString: productId)
    guard #available(iOS 15.0, *) else { pauseStoreSend("error", "\(id)\tiOS 15 required"); return }
    Task {
        guard let product = pauseStoreProducts[id] as? Product else {
            pauseStoreSend("error", "\(id)\tproduct not loaded"); return
        }
        do {
            let result = try await product.purchase()
            switch result {
            case .success(let verification): pauseStoreHandle(verification)
            case .pending: pauseStoreSend("pending", id)
            case .userCancelled: pauseStoreSend("cancelled", id)
            @unknown default: pauseStoreSend("error", "\(id)\tunknown purchase result")
            }
        } catch {
            pauseStoreSend("error", "\(id)\t\(error.localizedDescription)")
        }
    }
}

@_cdecl("PauseStore_Finish")
public func PauseStore_Finish(_ transactionId: UnsafePointer<CChar>) {
    let id = String(cString: transactionId)
    guard #available(iOS 15.0, *) else { return }
    Task {
        if let tx = pauseStorePending[id] as? Transaction {
            await tx.finish()
            pauseStorePending.removeValue(forKey: id)
        }
        pauseStoreSend("finished", id)
    }
}

@_cdecl("PauseStore_QueryEntitlements")
public func PauseStore_QueryEntitlements() {
    guard #available(iOS 15.0, *) else { pauseStoreSend("entitlements", ""); return }
    Task { await pauseStoreEntitlements() }
}

@_cdecl("PauseStore_Restore")
public func PauseStore_Restore() {
    guard #available(iOS 15.0, *) else { pauseStoreSend("restored", "error\tiOS 15 required"); return }
    Task {
        do {
            try await AppStore.sync()
            await pauseStoreEntitlements()
            pauseStoreSend("restored", "ok")
        } catch {
            pauseStoreSend("restored", "error\t\(error.localizedDescription)")
        }
    }
}
